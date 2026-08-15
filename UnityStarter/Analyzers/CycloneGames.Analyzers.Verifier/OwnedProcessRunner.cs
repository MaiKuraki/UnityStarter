using System.Diagnostics;
using System.Threading.Tasks;

namespace CycloneGames.Analyzers.Verifier
{
    internal sealed class ProcessRunResult
    {
        internal ProcessRunResult(int exitCode, bool timedOut, bool terminationConfirmed, string standardError)
        {
            ExitCode = exitCode;
            TimedOut = timedOut;
            TerminationConfirmed = terminationConfirmed;
            StandardError = standardError;
        }

        internal int ExitCode { get; }
        internal bool TimedOut { get; }
        internal bool TerminationConfirmed { get; }
        internal string StandardError { get; }
    }

    /// <summary>
    /// Runs an owned external process with a hard deadline. A timed-out process is killed together with its
    /// entire process tree; when termination cannot be confirmed the result reports it so the caller can fail
    /// closed exactly like the previous PowerShell verifier did.
    /// </summary>
    internal static class OwnedProcessRunner
    {
        internal static ProcessRunResult Run(
            string filePath,
            IReadOnlyList<string> arguments,
            int timeoutMilliseconds)
        {
            var startInfo = new ProcessStartInfo(filePath)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = false
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Failed to start process: " + filePath);

            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

            if (process.WaitForExit(timeoutMilliseconds))
            {
                string standardError = standardErrorTask.GetAwaiter().GetResult();
                return new ProcessRunResult(process.ExitCode, false, true, standardError);
            }

            bool terminationConfirmed;
            try
            {
                process.Kill(entireProcessTree: true);
                terminationConfirmed = process.WaitForExit(5_000);
            }
            catch
            {
                terminationConfirmed = false;
            }

            string timedOutError;
            try
            {
                timedOutError = standardErrorTask.GetAwaiter().GetResult();
            }
            catch
            {
                timedOutError = string.Empty;
            }

            return new ProcessRunResult(-1, true, terminationConfirmed, timedOutError);
        }
    }
}
