using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// File sink for measurement evidence. Batch-mode EditMode runs do not surface TestContext output in the
    /// editor log, so every measured row is appended to disk as CSV as well as written through Debug.Log.
    /// The target directory is supplied by the INPUTSYSTEM_PERF_OUTPUT environment variable so no machine
    /// specific path is stored in the repository.
    /// </summary>
    internal static class PerfOutput
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> HeaderWritten = new HashSet<string>(StringComparer.Ordinal);
        private static string _directory;

        internal static string RunId =>
            Environment.GetEnvironmentVariable("INPUTSYSTEM_PERF_RUN") ?? "adhoc";

        internal static string Directory
        {
            get
            {
                if (_directory != null) return _directory;
                string configured = Environment.GetEnvironmentVariable("INPUTSYSTEM_PERF_OUTPUT");
                _directory = string.IsNullOrEmpty(configured)
                    ? Path.Combine(Application.temporaryCachePath, "InputSystemPerf")
                    : configured;
                System.IO.Directory.CreateDirectory(_directory);
                return _directory;
            }
        }

        internal static string FilePath(string fileName)
        {
            return Path.Combine(Directory, fileName);
        }

        /// <summary>
        /// Appends one CSV row. The header is written once per process for each file, tracked per file so that a
        /// file created after the environment block still receives its column names.
        /// </summary>
        internal static void Row(string fileName, string header, IReadOnlyList<string> values)
        {
            var builder = new StringBuilder(256);
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(Escape(values[i]));
            }

            AppendLine(fileName, header, builder.ToString());
        }

        internal static void AppendLine(string fileName, string header, string line)
        {
            lock (Gate)
            {
                string path = FilePath(fileName);
                bool isNewFile = !File.Exists(path) || new FileInfo(path).Length == 0;
                bool needsHeader = !HeaderWritten.Contains(fileName) || isNewFile;
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    if (needsHeader && !string.IsNullOrEmpty(header))
                    {
                        writer.WriteLine(header);
                        HeaderWritten.Add(fileName);
                    }

                    writer.WriteLine(line);
                }
            }
        }

        internal static string Num(double value, int decimals = 3)
        {
            return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        internal static string Num(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Records the machine and engine facts that every measurement in this run depends on.
        /// </summary>
        internal static void WriteEnvironmentHeader(string fileName)
        {
            if (File.Exists(FilePath(fileName)) && new FileInfo(FilePath(fileName)).Length > 0) return;

            var lines = new List<string>
            {
                "runId=" + RunId,
                "utc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                "unityVersion=" + Application.unityVersion,
                "platform=" + Application.platform,
                "os=" + SystemInfo.operatingSystem,
                "deviceModel=" + SystemInfo.deviceModel,
                "processorType=" + SystemInfo.processorType,
                "processorCount=" + SystemInfo.processorCount,
                "processorFrequencyMHz=" + SystemInfo.processorFrequency,
                "systemMemoryMB=" + SystemInfo.systemMemorySize,
                "graphicsDevice=" + SystemInfo.graphicsDeviceName,
                "scriptingBackendMono=" + (IsMono() ? "true" : "false"),
                "batchMode=" + Application.isBatchMode,
                "runtimeVersion=" + Environment.Version,
                "is64BitProcess=" + Environment.Is64BitProcess
            };

            AppendLine(fileName, null, "# environment");
            for (int i = 0; i < lines.Count; i++) AppendLine(fileName, null, "# " + lines[i]);
            AppendLine(fileName, null, "# end environment");
        }

        private static bool IsMono()
        {
            Type monoType = Type.GetType("Mono.Runtime");
            return monoType != null;
        }

        internal static void Log(string message)
        {
            Debug.Log("[InputPerf] " + message);
        }
    }
}
