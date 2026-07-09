using System;
using System.Globalization;
using System.Text;
using System.Threading;

using Cysharp.Threading.Tasks;
using CycloneGames.IO.Runtime;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Writes bounded asset telemetry windows to a JSON Lines file using atomic replacement.
    /// </summary>
    public sealed class AssetRuntimeTelemetryFileSink
    {
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

            if (textBuffer == null)
            {
                throw new ArgumentNullException(nameof(textBuffer));
            }

            int count = recorder.CopyTo(sampleBuffer);
            textBuffer.Length = 0;

            for (int i = 0; i < count; i++)
            {
                AppendJsonLine(textBuffer, sampleBuffer[i]);
            }

            await FileUtility.WriteAllTextAtomicAsync(filePath, textBuffer.ToString(), cancellationToken);
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
            builder.Append("\"sequence\":");
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
            builder.Append('}');
            builder.AppendLine();
        }

    }
}
