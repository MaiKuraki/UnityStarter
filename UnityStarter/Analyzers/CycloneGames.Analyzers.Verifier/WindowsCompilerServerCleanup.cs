using System.Diagnostics;

namespace CycloneGames.Analyzers.Verifier
{
    /// <summary>
    /// Best-effort hygiene for the VBCSCompiler servers that Unity spawns inside its bundled .NET runtime.
    /// Only servers started after the verification launch and hosted inside the given editor directory are
    /// stopped, so unrelated compiler servers are never touched. Non-Windows platforms are no-ops; failures
    /// never fail the verification itself.
    /// </summary>
    internal static class WindowsCompilerServerCleanup
    {
        internal static void StopServersStartedAfter(string editorPath, DateTime launchTimeUtc)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            string editorDirectory = Path.GetDirectoryName(Path.GetFullPath(editorPath)) ??
                throw new InvalidOperationException("Editor path has no parent directory: " + editorPath);

            try
            {
                foreach (Process process in Process.GetProcessesByName("VBCSCompiler"))
                {
                    try
                    {
                        if (process.StartTime.ToUniversalTime() < launchTimeUtc)
                        {
                            continue;
                        }

                        string? modulePath = process.MainModule?.FileName;
                        if (string.IsNullOrEmpty(modulePath) ||
                            !modulePath.StartsWith(editorDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Best-effort hygiene: a process that cannot be inspected or stopped is left alone.
                    }
                }
            }
            catch
            {
                // Process enumeration itself can fail; hygiene must never fail the verification.
            }
        }
    }
}
