using System;
using System.Globalization;
using System.Text;
using System.Threading;

using Cysharp.Threading.Tasks;
using CycloneGames.IO;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Writes bounded asset telemetry windows to a JSON Lines file using atomic replacement.
    /// </summary>
    public sealed class AssetRuntimeTelemetryFileSink
    {
        public const int JSON_LINES_SCHEMA_VERSION = 1;

        private const int MAX_EXPORT_CHAR_COUNT = 16 * 1024 * 1024;
        public async UniTask<int> WriteJsonLinesAsync(
            string filePath,
            AssetRuntimeTelemetryRecorder recorder,
            AssetRuntimeTelemetrySample[] sampleBuffer,
            StringBuilder textBuffer,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Telemetry file path cannot be null or empty.", nameof(filePath));
            }

            if (recorder == null)
            {
                throw new ArgumentNullException(nameof(recorder));
            }

            if (sampleBuffer == null)
            {
                throw new ArgumentNullException(nameof(sampleBuffer));
            }

            if (sampleBuffer.Length == 0)
            {
                throw new ArgumentException("Telemetry sample buffer must contain at least one slot.", nameof(sampleBuffer));
            }

            if (sampleBuffer.Length > AssetRuntimeTelemetryOptions.MAX_CAPACITY)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleBuffer),
                    $"Telemetry export buffers cannot exceed {AssetRuntimeTelemetryOptions.MAX_CAPACITY} samples.");
            }

            if (textBuffer == null)
            {
                throw new ArgumentNullException(nameof(textBuffer));
            }

            int count = recorder.CopyTo(sampleBuffer);
            textBuffer.Length = 0;

            for (int i = 0; i < count; i++)
            {
                AppendJsonLine(textBuffer, sampleBuffer[i]);
                if (textBuffer.Length > MAX_EXPORT_CHAR_COUNT)
                {
                    throw new InvalidOperationException(
                        $"Telemetry export exceeds the {MAX_EXPORT_CHAR_COUNT}-character safety limit.");
                }
            }

            await SystemFileStore.Default.WriteTextAtomicallyAsync(
                filePath,
                textBuffer.ToString(),
                cancellationToken: cancellationToken);
            return count;
        }

        public static void AppendJsonLine(StringBuilder builder, AssetRuntimeTelemetrySample sample)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            AssetRuntimeCacheSnapshot snapshot = sample.Snapshot;
            builder.Append('{');
            builder.Append("\"schemaVersion\":");
            builder.Append(JSON_LINES_SCHEMA_VERSION);
            builder.Append(",\"sequence\":");
            builder.Append(sample.Sequence);
            builder.Append(",\"timestampUtcTicks\":");
            builder.Append(sample.TimestampUtcTicks);
            builder.Append(",\"packageName\":");
            JsonBuilderUtility.AppendString(builder, snapshot.PackageName);
            builder.Append(",\"providerName\":");
            JsonBuilderUtility.AppendString(builder, snapshot.ProviderName);
            builder.Append(",\"activeCount\":");
            builder.Append(snapshot.ActiveCount);
            builder.Append(",\"idleCount\":");
            builder.Append(snapshot.IdleCount);
            builder.Append(",\"idleBytesApprox\":");
            builder.Append(snapshot.IdleBytesApprox);
            builder.Append(",\"idleBytesBudget\":");
            builder.Append(snapshot.IdleBytesBudget);
            builder.Append(",\"idleBudgetUsage\":");
            builder.Append(snapshot.IdleBudgetUsage.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"idleBudgetExceeded\":");
            builder.Append(snapshot.IsIdleBudgetExceeded ? "true" : "false");
            builder.Append(",\"cacheLookupCount\":");
            builder.Append(snapshot.CacheLookupCount);
            builder.Append(",\"cacheHitCount\":");
            builder.Append(snapshot.CacheHitCount);
            builder.Append(",\"cacheHitRatio\":");
            builder.Append(snapshot.CacheHitRatio.ToString("R", CultureInfo.InvariantCulture));
            builder.Append(",\"activeHitCount\":");
            builder.Append(snapshot.ActiveHitCount);
            builder.Append(",\"idleHitCount\":");
            builder.Append(snapshot.IdleHitCount);
            builder.Append(",\"cacheMissCount\":");
            builder.Append(snapshot.CacheMissCount);
            builder.Append(",\"idleAdmissionCount\":");
            builder.Append(snapshot.IdleAdmissionCount);
            builder.Append(",\"admissionRejectionCount\":");
            builder.Append(snapshot.AdmissionRejectionCount);
            builder.Append(",\"failedOperationRejectionCount\":");
            builder.Append(snapshot.FailedOperationRejectionCount);
            builder.Append(",\"metadataOverflowRejectionCount\":");
            builder.Append(snapshot.MetadataOverflowRejectionCount);
            builder.Append(",\"unknownFootprintRejectionCount\":");
            builder.Append(snapshot.UnknownFootprintRejectionCount);
            builder.Append(",\"oversizeRejectionCount\":");
            builder.Append(snapshot.OversizeRejectionCount);
            builder.Append(",\"footprintEstimationFailureCount\":");
            builder.Append(snapshot.FootprintEstimationFailureCount);
            builder.Append(",\"evictionCount\":");
            builder.Append(snapshot.EvictionCount);
            builder.Append(",\"capacityEvictionCount\":");
            builder.Append(snapshot.CapacityEvictionCount);
            builder.Append(",\"memoryBudgetEvictionCount\":");
            builder.Append(snapshot.MemoryBudgetEvictionCount);
            builder.Append(",\"retentionEvictionCount\":");
            builder.Append(snapshot.RetentionEvictionCount);
            builder.Append(",\"explicitEvictionCount\":");
            builder.Append(snapshot.ExplicitEvictionCount);
            builder.Append(",\"evictedBytesApprox\":");
            builder.Append(snapshot.EvictedBytesApprox);
            builder.Append(",\"providerReleaseFailureCount\":");
            builder.Append(snapshot.ProviderReleaseFailureCount);
            builder.Append(",\"peakActiveCount\":");
            builder.Append(snapshot.PeakActiveCount);
            builder.Append(",\"peakIdleCount\":");
            builder.Append(snapshot.PeakIdleCount);
            builder.Append(",\"peakIdleBytesApprox\":");
            builder.Append(snapshot.PeakIdleBytesApprox);
            builder.Append('}');
            builder.AppendLine();
        }

    }
}
