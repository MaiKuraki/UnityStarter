using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Build.VersionControl.Editor
{
    public class VersionControlProviderPerforce : VersionControlProviderBase
    {
        private const string P4_EXECUTABLE = "p4";
        private const int PROCESS_TIMEOUT_MS = 10000;

        private static readonly Regex ChangeNumberRegex = new Regex(@"Change\s+(\d+)", RegexOptions.Compiled);
        private static readonly Regex ChangeDateRegex = new Regex(@"Change\s+\d+\s+on\s+(\d{4}/\d{2}/\d{2})", RegexOptions.Compiled);

        public override string GetCommitHash()
        {
            string output = RunP4Command("changes -m 1 -s submitted");
            if (string.IsNullOrEmpty(output))
                return "0";

            Match match = ChangeNumberRegex.Match(output);
            return match.Success ? match.Groups[1].Value : "0";
        }

        public override string GetCommitCount()
        {
            // Perforce changelist numbers are monotonically increasing counters.
            // The latest submitted changelist serves as the de-facto "commit count".
            return GetCommitHash();
        }

        public override string GetBranchName()
        {
            string output = RunP4Command("client -o");
            if (string.IsNullOrEmpty(output))
                return "unknown";

            // Extract Stream field (if using streams) or Client name
            var streamMatch = Regex.Match(output, @"^Stream:\s+(.+)$", RegexOptions.Multiline);
            if (streamMatch.Success)
                return streamMatch.Groups[1].Value.Trim();

            var clientMatch = Regex.Match(output, @"^Client:\s+(.+)$", RegexOptions.Multiline);
            return clientMatch.Success ? clientMatch.Groups[1].Value.Trim() : "unknown";
        }

        public override string GetCommitDate()
        {
            string output = RunP4Command("changes -m 1 -s submitted");
            if (string.IsNullOrEmpty(output))
                return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Match match = ChangeDateRegex.Match(output);
            if (match.Success)
            {
                string rawDate = match.Groups[1].Value;
                if (DateTime.TryParse(rawDate, out DateTime parsed))
                    return parsed.ToString("yyyy-MM-dd HH:mm:ss");
                return rawDate;
            }

            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string RunP4Command(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = P4_EXECUTABLE,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(PROCESS_TIMEOUT_MS);

                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        UnityEngine.Debug.LogWarning($"[VC] P4 command failed (exit {process.ExitCode}): p4 {arguments}\n{error}");
                        return null;
                    }

                    return output.Trim();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[VC] P4 command error: p4 {arguments}\n{ex.Message}");
                return null;
            }
        }
    }
}
