using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace Build.VersionControl.Editor
{
    public class VersionControlProviderGit : VersionControlProviderBase
    {
        private const string GIT_EXECUTABLE = "git";
        private const int PROCESS_TIMEOUT_MS = 10000;

        public override string GetCommitHash()
        {
            return RunGitCommand("rev-parse --short=7 HEAD") ?? "unknown";
        }

        public override string GetCommitCount()
        {
            return RunGitCommand("rev-list --count HEAD") ?? "0";
        }

        public override string GetBranchName()
        {
            return RunGitCommand("rev-parse --abbrev-ref HEAD") ?? "unknown";
        }

        public override string GetCommitDate()
        {
            return RunGitCommand("log -1 --format=%ci") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string RunGitCommand(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = GIT_EXECUTABLE,
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
                        UnityEngine.Debug.LogWarning($"[VC] Git command failed (exit {process.ExitCode}): git {arguments}\n{error}");
                        return null;
                    }

                    return output.Trim();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[VC] Git command error: git {arguments}\n{ex.Message}");
                return null;
            }
        }
    }
}
