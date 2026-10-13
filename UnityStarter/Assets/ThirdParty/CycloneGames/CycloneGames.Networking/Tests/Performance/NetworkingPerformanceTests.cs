using System;

using CycloneGames.DeterministicMath;
using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Transports;

using NUnit.Framework;

using Unity.PerformanceTesting;

namespace CycloneGames.Networking.Tests.Performance
{
    public sealed class NetworkingPerformanceTests
    {
        private const int WARMUP_COUNT = 5;
        private const int MEASUREMENT_COUNT = 20;
        private const int ROUNDTRIP_MESSAGES = 2_048;
        private const int FRAGMENT_MESSAGES = 256;
        private const int QUEUE_OPERATIONS = 8_192;
        private const int VECTOR_BATCH = 4_096;
        private const int SMALL_PAYLOAD_BYTES = 200;
        private const int LARGE_PAYLOAD_BYTES = 4_096;

        private static int s_receiveSink;
        private static long s_hashSink;

        [Test, Performance]
        public void LocalLoop_SmallMessage_Roundtrip()
        {
            RunWithPair((server, client, payload) =>
            {
                for (int i = 0; i < ROUNDTRIP_MESSAGES; i++)
                {
                    client.Send(null, payload, client.GetChannelId(NetworkChannel.Reliable));
                    server.PollEvents();
                    client.PollEvents();
                }
            }, SMALL_PAYLOAD_BYTES);
        }

        [Test, Performance]
        public void Fragmentation_4K_SendAndReassemble()
        {
            RunWithFragmentedPair((server, client, payload) =>
            {
                for (int i = 0; i < FRAGMENT_MESSAGES; i++)
                {
                    client.Send(null, payload, client.GetChannelId(NetworkChannel.Reliable));
                    server.PollEvents();
                }
            }, LARGE_PAYLOAD_BYTES);
        }

        [Test, Performance]
        public void BoundedSendQueue_BurstEnqueueDequeue()
        {
            using var queue = new BoundedSendQueue(256);
            var payload = new ArraySegment<byte>(MakePayload(SMALL_PAYLOAD_BYTES, 11));
            Measure.Method(() =>
            {
                for (int i = 0; i < QUEUE_OPERATIONS; i++)
                {
                    queue.Enqueue(payload, 0);
                    if (queue.Count >= 128)
                    {
                        while (queue.TryDequeue(out ArraySegment<byte> segment, out _))
                        {
                            s_receiveSink += segment.Count;
                        }
                    }
                }
                queue.Clear();
            })
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(1)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void FixedVector3_EncodeDecode()
        {
            NetworkBuffer buffer = NetworkBufferPool.Get();
            try
            {
                var value = new NetworkFixedVector3(
                    FPInt64.FromFloat(123.5f), FPInt64.FromFloat(-8.25f), FPInt64.FromFloat(4096.125f));
                Measure.Method(() =>
                {
                    for (int i = 0; i < VECTOR_BATCH; i++)
                    {
                        buffer.Reset();
                        value.WriteTo(buffer);
                        buffer.FlipForRead();
                        NetworkFixedVector3 decoded = NetworkFixedVector3.ReadFrom(buffer);
                        s_hashSink ^= decoded.X.RawValue ^ decoded.Y.RawValue ^ decoded.Z.RawValue;
                    }
                })
                    .WarmupCount(WARMUP_COUNT)
                    .MeasurementCount(MEASUREMENT_COUNT)
                    .IterationsPerMeasurement(1)
                    .GC()
                    .Run();
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [Test, Performance]
        public void QuantizedVector3_EncodeDecode()
        {
            NetworkBuffer buffer = NetworkBufferPool.Get();
            try
            {
                var value = new NetworkVector3(123.5f, -8.25f, 4096.125f);
                Measure.Method(() =>
                {
                    for (int i = 0; i < VECTOR_BATCH; i++)
                    {
                        buffer.Reset();
                        NetworkVector3Quantization.WriteQuantized(buffer, value);
                        buffer.FlipForRead();
                        NetworkVector3 decoded = NetworkVector3Quantization.ReadQuantized(buffer);
                        s_hashSink ^= (long)(decoded.X + decoded.Y + decoded.Z);
                    }
                })
                    .WarmupCount(WARMUP_COUNT)
                    .MeasurementCount(MEASUREMENT_COUNT)
                    .IterationsPerMeasurement(1)
                    .GC()
                    .Run();
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [Test]
        public void LocalLoop_Roundtrip_SteadyStateAllocatesZeroBytes()
        {
            void Batch(LocalLoopTransport server, LocalLoopTransport client, ArraySegment<byte> payload)
            {
                for (int i = 0; i < 512; i++)
                {
                    client.Send(null, payload, client.GetChannelId(NetworkChannel.Reliable));
                    server.PollEvents();
                }
            }

            using var server = CreateServer();
            using var client = CreateClient();
            var payload = new ArraySegment<byte>(MakePayload(SMALL_PAYLOAD_BYTES, 21));
            Batch(server, client, payload);

            long before = GC.GetAllocatedBytesForCurrentThread();
            Batch(server, client, payload);
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocatedBytes, Is.EqualTo(0), "Steady-state loopback roundtrip must not allocate.");
        }

        [Test]
        public void BoundedSendQueue_SteadyStateAllocatesZeroBytes()
        {
            using var queue = new BoundedSendQueue(128);
            var payload = new ArraySegment<byte>(MakePayload(SMALL_PAYLOAD_BYTES, 31));

            void Batch()
            {
                for (int i = 0; i < 512; i++)
                {
                    queue.Enqueue(payload, 0);
                    while (queue.TryDequeue(out ArraySegment<byte> segment, out _))
                    {
                        s_receiveSink += segment.Count;
                    }
                }
            }

            Batch();

            long before = GC.GetAllocatedBytesForCurrentThread();
            Batch();
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocatedBytes, Is.EqualTo(0), "Steady-state queue churn must not allocate.");
        }

        [Test]
        public void FixedVector3_Codec_SteadyStateAllocatesZeroBytes()
        {
            NetworkBuffer buffer = NetworkBufferPool.Get();
            var value = new NetworkFixedVector3(
                FPInt64.FromFloat(1.5f), FPInt64.FromFloat(2.5f), FPInt64.FromFloat(3.5f));

            void Batch()
            {
                for (int i = 0; i < 512; i++)
                {
                    buffer.Reset();
                    value.WriteTo(buffer);
                    buffer.FlipForRead();
                    s_hashSink ^= NetworkFixedVector3.ReadFrom(buffer).X.RawValue;
                }
            }

            try
            {
                Batch();

                long before = GC.GetAllocatedBytesForCurrentThread();
                Batch();
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocatedBytes, Is.EqualTo(0), "Fixed-point codec must not allocate.");
            }
            finally
            {
                buffer.Dispose();
            }
        }

        private static LocalLoopTransport CreateServer()
        {
            var server = new LocalLoopTransport();
            server.OnDataReceived += OnLoopData;
            server.StartServer();
            return server;
        }

        private static LocalLoopTransport CreateClient()
        {
            var client = new LocalLoopTransport();
            client.StartClient(string.Empty);
            return client;
        }

        private static void OnLoopData(INetConnection connection, ArraySegment<byte> payload, int channelId)
        {
            s_receiveSink += payload.Count;
        }

        private static void RunWithPair(
            Action<LocalLoopTransport, LocalLoopTransport, ArraySegment<byte>> body, int payloadBytes)
        {
            using var server = CreateServer();
            using var client = CreateClient();
            var payload = new ArraySegment<byte>(MakePayload(payloadBytes, 41));
            Measure.Method(() => body(server, client, payload))
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(1)
                .GC()
                .Run();
        }

        private static void RunWithFragmentedPair(
            Action<FragmentationTransport, FragmentationTransport, ArraySegment<byte>> body, int payloadBytes)
        {
            using var serverInner = CreateServer();
            using var clientInner = CreateClient();
            using var server = new FragmentationTransport(serverInner, FragmentationOptions.Default);
            using var client = new FragmentationTransport(clientInner, FragmentationOptions.Default);
            var payload = new ArraySegment<byte>(MakePayload(payloadBytes, 51));
            Measure.Method(() => body(server, client, payload))
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(1)
                .GC()
                .Run();
        }

        private static byte[] MakePayload(int length, int seed)
        {
            var data = new byte[length];
            var rng = new Random(seed);
            rng.NextBytes(data);
            return data;
        }
    }
}
