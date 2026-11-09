using System;
using System.Globalization;
using System.IO;
using System.Text;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests.Evidence
{
    /// <summary>
    /// Persistent mirror of the evidence lines, written to &lt;project&gt;/Logs/AnimationRuntimeEvidence.md.
    ///
    /// The project log channel is the primary sink, but <see cref="LogChannel.Create(string)"/> resolves the
    /// process writer on every call and the CycloneGames.Logging backend does not install one in a batch-mode
    /// test run, so a channel-only sink drops every line. This file survives the run and is the artefact the
    /// review quotes.
    ///
    /// The file is truncated on the first line of each run; a mid-run domain reload would restart it, which is
    /// acceptable for a mirror. Write failures are ignored so a logging problem can never fail a test.
    /// </summary>
    internal static class EvidenceFileSink
    {
        private static readonly object Gate = new object();

        private static string _path;
        private static bool _started;
        private static int _failureCount;

        internal static string Path
        {
            get
            {
                if (_path == null)
                {
                    _path = System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(Application.dataPath, "..", "Logs", "AnimationRuntimeEvidence.md"));
                }

                return _path;
            }
        }

        internal static void Write(string label, string message, LogChannel channel, bool hasProcessWriter)
        {
            lock (Gate)
            {
                try
                {
                    if (!_started)
                    {
                        string directory = System.IO.Path.GetDirectoryName(Path);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.WriteAllText(Path, BuildHeader(channel, hasProcessWriter), new UTF8Encoding(false));
                        _started = true;
                    }

                    File.AppendAllText(Path, "| " + label + " | " + message + " |\n", new UTF8Encoding(false));
                }
                catch (Exception)
                {
                    // A mirror must never fail a test. The run-end check asserts the file exists.
                    _failureCount++;
                }
            }
        }

        private static string BuildHeader(LogChannel channel, bool hasProcessWriter)
        {
            var builder = new StringBuilder(512);
            builder.Append("# AnimationRuntime evidence (raw measurements)\n\n");
            builder.Append("Run (local time): ")
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("Unity: ").Append(Application.unityVersion).Append('\n');
            builder.Append("Platform: ").Append(Application.platform)
                .Append(" / isPlaying=").Append(Application.isPlaying).Append('\n');
            builder.Append("Process log writer installed: ").Append(hasProcessWriter).Append('\n');
            builder.Append("Evidence channel Info enabled: ")
                .Append(channel.IsEnabled(LogSeverity.Info)).Append('\n');
            builder.Append("Evidence channel category: ").Append(channel.Category).Append('\n');
            builder.Append("\n| label | measurement |\n| --- | --- |\n");
            return builder.ToString();
        }
    }
}
