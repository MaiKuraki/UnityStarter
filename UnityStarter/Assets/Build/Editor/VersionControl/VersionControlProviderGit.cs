using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace Build.VersionControl.Editor
{
    public class VersionControlProviderGit : VersionControlProviderBase
    {
        private const string GIT_EXECUTABLE = "git";
        private const int PROCESS_TIMEOUT_MS = 10000;

        private static string _projectRoot;

        private static string GetProjectRoot()
        {
            if (_projectRoot == null)
            {
                _projectRoot = FindGitRoot() ?? Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }
            return _projectRoot;
        }

        /// <summary>
        /// Walks up from Assets/ to find the .git directory.
        /// This correctly handles Unity projects nested inside a Git repo.
        /// </summary>
        private static string FindGitRoot()
        {
            string dir = Path.GetFullPath(Application.dataPath);
            string root = Path.GetPathRoot(dir);
            while (dir != null && dir.Length >= root.Length)
            {
                string gitPath = Path.Combine(dir, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

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
                string projectRoot = GetProjectRoot();

                // Primary: use -c safe.directory to trust the repo for this invocation.
                // If Git rejects it (exit 128 = fatal, e.g. cross-drive / cross-user),
                // permanently register the directory in global Git config and retry.
                string result = TryRun(arguments, projectRoot);
                if (result != null)
                    return result;

                UnityEngine.Debug.Log($"[VC] Primary attempt failed, registering safe.directory and retrying...");
                RegisterSafeDirectory(projectRoot);
                return TryRun(arguments, null);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[VC] Git command error: git {arguments}\n{ex.Message}");
                return null;
            }
        }

        private static string TryRun(string arguments, string safeDir)
        {
            string args = safeDir != null
                ? $"-c safe.directory={safeDir.Replace('\\', '/')} {arguments}"
                : arguments;

            var startInfo = new ProcessStartInfo
            {
                FileName = GIT_EXECUTABLE,
                Arguments = args,
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
                    UnityEngine.Debug.LogWarning($"[VC] git {args} → exit {process.ExitCode}\n{error}");
                    return null;
                }

                return output.Trim();
            }
        }

        private static void RegisterSafeDirectory(string projectRoot)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = GIT_EXECUTABLE,
                    Arguments = $"config --global --add safe.directory \"{projectRoot}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    process.WaitForExit(PROCESS_TIMEOUT_MS);

                    if (process.ExitCode == 0)
                    {
                        UnityEngine.Debug.Log($"[VC] Registered safe.directory for: {projectRoot}");
                    }
                    else
                    {
                        string error = process.StandardError.ReadToEnd();
                        UnityEngine.Debug.LogWarning($"[VC] Failed to register safe.directory: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[VC] Failed to register safe.directory: {ex.Message}");
            }
        }
    }
}
