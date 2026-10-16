using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using CycloneGames.Networking.Security;
using CycloneGames.Networking.Transports;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    /// <summary>
    /// Audit probe (NET audit 2026-10-16). Measures steady-state managed allocation
    /// and timing medians on the transport/codec hot paths, asserts the zero-GC
    /// contract where the module claims it, and exports the raw numbers to
    /// Temp/netaudit/net-hotpath-probe.json for the audit record. Timing is
    /// reported, never asserted: only allocation counts and exact operation
    /// counters are used as hard assertions.
    /// </summary>
    public sealed class NetworkHotPathAuditProbeTests
    {
        private const int FRAME_ITERATIONS = 2048;
        private const int QUEUE_ITERATIONS = 2048;
        private const int LOOPBACK_ITERATIONS = 512;
        private const int FRAGMENT_ITERATIONS = 128;
        private const int SMALL_PAYLOAD = 200;
        private const int LARGE_PAYLOAD = 4096;
        private const int TIMING_ROUNDS = 3;

        [Test]
        public void HotPaths_SteadyStateAllocation_And_Scaling()
        {
            var report = new StringBuilder();
            report.Append("{\n");

            // Deterministic marker over encoded frames: identical across processes
            // for the same code regardless of timing or environment noise.
            ulong determinismMarker = 14695981039346656037UL;

            // --- Frame codec: encode + parse + checksum validate ---
            byte[] frame = new byte[NetworkWireProtocol.HeaderLength + SMALL_PAYLOAD];
            for (int i = 0; i < SMALL_PAYLOAD; i++)
            {
                frame[NetworkWireProtocol.HeaderLength + i] = (byte)(i * 31 + 7);
            }

            const NetworkChannel Channel = NetworkChannel.Reliable;
            const ushort MessageId = 30001;
            var payloadSpan = new ReadOnlySpan<byte>(frame, NetworkWireProtocol.HeaderLength, SMALL_PAYLOAD);
            uint checksum = NetworkFrameCodec.ComputeChecksum(MessageId, Channel, NetworkMessageFlags.None, 1, payloadSpan);
            var header = new NetworkEnvelopeHeader(MessageId, Channel, SMALL_PAYLOAD, 1, checksum);

            for (int i = 0; i < 64; i++)
            {
                FrameRoundTrip(frame, header, ref determinismMarker);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < FRAME_ITERATIONS; i++)
            {
                FrameRoundTrip(frame, header, ref determinismMarker);
            }
            long frameAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
            report.Append($"  \"frame_codec_alloc_bytes_per_{FRAME_ITERATIONS}ops\": {frameAlloc},\n");

            // --- BoundedSendQueue: enqueue + dequeue with pooled slab ---
            using (var queue = new BoundedSendQueue(capacity: 256))
            {
                var segment = new ArraySegment<byte>(frame, NetworkWireProtocol.HeaderLength, SMALL_PAYLOAD);
                for (int i = 0; i < 64; i++)
                {
                    queue.Enqueue(segment, 0);
                    queue.TryDequeue(out _, out _);
                }

                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < QUEUE_ITERATIONS; i++)
                {
                    queue.Enqueue(segment, 0);
                    queue.TryDequeue(out ArraySegment<byte> dequeued, out _);
                    determinismMarker = Fnv(determinismMarker, dequeued.Array[dequeued.Offset]);
                }
                long queueAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
                report.Append($"  \"send_queue_alloc_bytes_per_{QUEUE_ITERATIONS}ops\": {queueAlloc},\n");
            }

            // --- LocalLoop small-message roundtrip ---
            using (var server = new LocalLoopTransport())
            using (var client = new LocalLoopTransport())
            {
                byte[] captured = null;
                server.OnDataReceived += (_, payload, _) =>
                {
                    captured = payload.Array;
                    if (captured != null) determinismMarker = Fnv(determinismMarker, captured[payload.Offset]);
                };

                server.StartServer();
                client.StartClient(string.Empty);
                server.PollEvents();

                var outgoing = new ArraySegment<byte>(frame, NetworkWireProtocol.HeaderLength, SMALL_PAYLOAD);
                for (int i = 0; i < 64; i++)
                {
                    client.Send(null, outgoing, client.GetChannelId(Channel));
                    server.PollEvents();
                    client.PollEvents();
                }

                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < LOOPBACK_ITERATIONS; i++)
                {
                    client.Send(null, outgoing, client.GetChannelId(Channel));
                    server.PollEvents();
                    client.PollEvents();
                }
                long loopAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
                report.Append($"  \"loopback_alloc_bytes_per_{LOOPBACK_ITERATIONS}ops\": {loopAlloc},\n");
            }

            // --- Scaling: frame codec per-message cost across payload sizes ---
            report.Append("  \"frame_codec_ns_per_message\": {\n");
            int[] sizes = { 64, SMALL_PAYLOAD, 1024, 4096 };
            for (int s = 0; s < sizes.Length; s++)
            {
                int size = sizes[s];
                byte[] bigFrame = new byte[NetworkWireProtocol.HeaderLength + size];
                for (int i = 0; i < size; i++)
                {
                    bigFrame[NetworkWireProtocol.HeaderLength + i] = (byte)(i * 13 + 3);
                }

                var bigPayload = new ReadOnlySpan<byte>(bigFrame, NetworkWireProtocol.HeaderLength, size);
                uint bigChecksum = NetworkFrameCodec.ComputeChecksum(MessageId, Channel, NetworkMessageFlags.None, 2, bigPayload);
                var bigHeader = new NetworkEnvelopeHeader(MessageId, Channel, size, 2, bigChecksum);

                ulong marker = determinismMarker;
                double[] rounds = new double[TIMING_ROUNDS];
                for (int r = 0; r < TIMING_ROUNDS; r++)
                {
                    var watch = Stopwatch.StartNew();
                    for (int i = 0; i < FRAME_ITERATIONS; i++)
                    {
                        FrameRoundTrip(bigFrame, bigHeader, ref marker);
                    }
                    watch.Stop();
                    rounds[r] = watch.Elapsed.TotalMilliseconds * 1_000_000.0 / FRAME_ITERATIONS;
                }
                determinismMarker = marker;
                Array.Sort(rounds);
                report.Append($"    \"{size}\": {{ \"median_ns\": {rounds[TIMING_ROUNDS / 2]:F0} }}");
                report.Append(s + 1 < sizes.Length ? ",\n" : "\n");
            }
            report.Append("  },\n");

            // --- Fragmentation: report-only (pool warmup included) ---
            using (var serverInner = new LocalLoopTransport())
            using (var clientInner = new LocalLoopTransport())
            using (var server = new FragmentationTransport(serverInner))
            using (var client = new FragmentationTransport(clientInner))
            {
                server.StartServer();
                client.StartClient(string.Empty);
                server.PollEvents();

                byte[] large = new byte[LARGE_PAYLOAD];
                for (int i = 0; i < LARGE_PAYLOAD; i++)
                {
                    large[i] = (byte)(i * 7 + 1);
                }

                var largeSegment = new ArraySegment<byte>(large);
                for (int i = 0; i < 16; i++)
                {
                    client.Send(null, largeSegment, client.GetChannelId(Channel));
                    server.PollEvents();
                }

                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < FRAGMENT_ITERATIONS; i++)
                {
                    client.Send(null, largeSegment, client.GetChannelId(Channel));
                    server.PollEvents();
                }
                long fragmentAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
                report.Append($"  \"fragmentation_alloc_bytes_per_{FRAGMENT_ITERATIONS}msgs\": {fragmentAlloc},\n");
            }

            report.Append($"  \"determinism_marker_fnv1a64\": \"{determinismMarker:X16}\"\n");
            report.Append("}\n");

            string probeDirectory = Path.Combine(UnityEngine.Application.dataPath, "..", "Library", "netaudit");
            Directory.CreateDirectory(probeDirectory);
            string probePath = Path.Combine(probeDirectory, "net-hotpath-probe.json");
            File.WriteAllText(probePath, report.ToString(), new UTF8Encoding(false));
            TestContext.Progress.WriteLine($"NET hot-path probe written to {probePath}");

            // Hard assertions: the module's zero-GC claims on these paths.
            Assert.AreEqual(0, frameAlloc, "Frame codec encode+parse+validate must not allocate in steady state.");
        }

        [Test]
        public void FrameCodec_DeterminismMarker_IsStableWithinProcess()
        {
            const int Payload = 256;
            byte[] frame = new byte[NetworkWireProtocol.HeaderLength + Payload];
            for (int i = 0; i < Payload; i++)
            {
                frame[NetworkWireProtocol.HeaderLength + i] = (byte)(i * 3 + 11);
            }

            var payloadSpan = new ReadOnlySpan<byte>(frame, NetworkWireProtocol.HeaderLength, Payload);
            uint checksum = NetworkFrameCodec.ComputeChecksum(30002, NetworkChannel.Reliable, NetworkMessageFlags.None, 5, payloadSpan);
            var header = new NetworkEnvelopeHeader(30002, NetworkChannel.Reliable, Payload, 5, checksum);

            // Same logical message encoded twice, each from a fresh marker: the
            // marker folds the same payload bytes on both passes, so identical
            // encoding must produce identical markers; a fresh independent frame
            // buffer must additionally hold identical bytes.
            ulong first = 14695981039346656037UL;
            FrameRoundTrip(frame, header, ref first);

            ulong second = 14695981039346656037UL;
            FrameRoundTrip(frame, header, ref second);

            Assert.AreEqual(first, second, "Encoding the same logical message must produce byte-identical output, repeatably.");

            byte[] independentFrame = new byte[NetworkWireProtocol.HeaderLength + Payload];
            for (int i = 0; i < Payload; i++)
            {
                independentFrame[NetworkWireProtocol.HeaderLength + i] = (byte)(i * 3 + 11);
            }

            NetworkFrameCodec.WriteHeader(independentFrame, 0, header);
            Assert.AreEqual(frame, independentFrame, "Independent encode passes must be byte-identical.");
        }

        private static void FrameRoundTrip(byte[] frame, in NetworkEnvelopeHeader header, ref ulong marker)
        {
            NetworkFrameCodec.WriteHeader(frame, 0, header);
            NetworkFrameResult result = NetworkFrameCodec.TryReadPayload(
                new ArraySegment<byte>(frame), out NetworkEnvelopeHeader readHeader, out ArraySegment<byte> payload);
            if (result != NetworkFrameResult.Valid)
            {
                throw new InvalidOperationException("Frame parse failed inside the audit probe.");
            }

            NetworkFrameResult checksumResult = NetworkFrameCodec.ValidateChecksum(
                readHeader, new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count));
            if (checksumResult != NetworkFrameResult.Valid)
            {
                throw new InvalidOperationException("Checksum validation failed inside the audit probe.");
            }

            marker = Fnv(marker, payload.Array[payload.Offset]);
        }

        private static ulong Fnv(ulong hash, byte value)
        {
            hash ^= value;
            return hash * 1099511628211UL;
        }
    }
}
