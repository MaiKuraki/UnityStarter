using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AnimationRuntime.Tests
{
    /// <summary>
    /// Append-only writer for the test log. A PlayMode run reloads the domain, so results are written to disk
    /// instead of memory. Write failures are ignored.
    /// </summary>
    public static class TestRunLog
    {
        private static readonly object Gate = new object();
        private static string _logPath;

        public static string Path
        {
            get
            {
                if (_logPath == null)
                {
#if UNITY_EDITOR
                    // Project Logs folder: Temp is deleted when a batch-mode run exits, which would drop every
                    // measurement the stress suites publish.
                    _logPath = System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(Application.dataPath, "..", "Logs", "AnimationRuntimeTestLog.md"));
#else
                    // A built player cannot reliably write next to its data folder; persistentDataPath is the
                    // only location guaranteed writable on every platform.
                    _logPath = System.IO.Path.Combine(Application.persistentDataPath, "AnimationRuntimeTestLog.md");
#endif
                }

                return _logPath;
            }
        }

        /// <summary>Appends one measurement or marker line. Used by stress tests to publish numbers.</summary>
        public static void Note(string label, string message)
        {
            Append("| Note | " + Sanitize(label) + " | " + Sanitize(message) + " |");
        }

        internal static void Append(string line)
        {
            try
            {
                lock (Gate)
                {
                    string directory = System.IO.Path.GetDirectoryName(Path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        System.IO.Directory.CreateDirectory(directory);
                    }

                    File.AppendAllText(Path, line + "\n", new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must not fail a test.
            }
        }

        internal static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        }
    }

    /// <summary>
    /// Base fixture that records every test outcome plus its measured duration.
    ///
    /// Timing is measured here rather than read from <c>TestContext.Result</c>: the NUnit version shipped with
    /// Unity's Test Framework does not expose a duration on the result adapter.
    /// </summary>
    public abstract class LoggedTestBase
    {
        private long _startTimestamp;

        [SetUp]
        public void MarkStart()
        {
            _startTimestamp = Stopwatch.GetTimestamp();
        }

        [TearDown]
        public void AppendResultToLog()
        {
            TestContext context = TestContext.CurrentContext;
            TestContext.ResultAdapter result = context.Result;

            long elapsed = Stopwatch.GetTimestamp() - _startTimestamp;
            double seconds = elapsed / (double)Stopwatch.Frequency;

            string mode = Application.isPlaying ? "PlayMode" : "EditMode";
            TestRunLog.Append("| " + mode
                + " | " + TestRunLog.Sanitize(context.Test.ClassName)
                + " | " + TestRunLog.Sanitize(context.Test.Name)
                + " | " + result.Outcome.Status
                + " | " + seconds.ToString("0.000", CultureInfo.InvariantCulture) + "s"
                + " | " + TestRunLog.Sanitize(result.Message)
                + " |");
        }
    }
}
