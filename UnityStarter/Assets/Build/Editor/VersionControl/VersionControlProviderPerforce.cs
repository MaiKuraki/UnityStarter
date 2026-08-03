using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Build.VersionControl.Editor
{
    internal sealed class VersionControlProviderPerforce : IVersionControlProvider
    {
        private const string P4Executable = "p4";
        private const int ProcessTimeoutMilliseconds = 10000;
        private const int MaximumProcessOutputCharacters = 64 * 1024;

        private static readonly Regex ChangeNumberRegex = new Regex(@"Change\s+(\d+)", RegexOptions.Compiled);
        private static readonly Regex ChangeDateRegex = new Regex(@"Change\s+\d+\s+on\s+(\d{4}/\d{2}/\d{2})", RegexOptions.Compiled);

        public VersionControlMetadata Capture()
        {
            string changeOutput = RunP4Command("changes -m 1 -s submitted");
            Match changeMatch = ChangeNumberRegex.Match(changeOutput);
            if (!changeMatch.Success)
            {
                throw new InvalidOperationException(
                    "Perforce did not return a latest submitted changelist.");
            }

            string change = changeMatch.Groups[1].Value;
            if (!long.TryParse(
                    change,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long changeNumber)
                || changeNumber <= 0)
            {
                throw new InvalidOperationException(
                    "Perforce returned an invalid submitted changelist number.");
            }

            Match dateMatch = ChangeDateRegex.Match(changeOutput);
            if (!dateMatch.Success
                || !DateTime.TryParseExact(
                    dateMatch.Groups[1].Value,
                    "yyyy/MM/dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsedDate))
            {
                throw new InvalidOperationException(
                    "Perforce returned an invalid submitted changelist date.");
            }

            string clientOutput = RunP4Command("client -o");
            Match streamMatch = Regex.Match(
                clientOutput,
                @"^Stream:\s+(.+)$",
                RegexOptions.Multiline);
            Match clientMatch = Regex.Match(
                clientOutput,
                @"^Client:\s+(.+)$",
                RegexOptions.Multiline);
            string branch = streamMatch.Success
                ? streamMatch.Groups[1].Value.Trim()
                : clientMatch.Success
                    ? clientMatch.Groups[1].Value.Trim()
                    : string.Empty;
            if (string.IsNullOrWhiteSpace(branch) || branch.Length > 512)
            {
                throw new InvalidOperationException(
                    "Perforce client metadata does not contain a bounded Stream or Client name.");
            }

            return new VersionControlMetadata(
                "Perforce",
                change,
                change,
                branch,
                parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        private static string RunP4Command(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = P4Executable,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(ProcessTimeoutMilliseconds))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                        }

                        process.WaitForExit();
                        throw new TimeoutException(
                            $"Perforce command timed out after {ProcessTimeoutMilliseconds} ms: p4 {arguments}");
                    }

                    process.WaitForExit();
                    string output = outputTask.GetAwaiter().GetResult();
                    string error = errorTask.GetAwaiter().GetResult();

                    if (output.Length > MaximumProcessOutputCharacters
                        || error.Length > MaximumProcessOutputCharacters)
                    {
                        throw new InvalidOperationException(
                            $"Perforce command exceeded its output budget: p4 {arguments}");
                    }

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException(
                            $"Perforce command failed (exit {process.ExitCode}): p4 {arguments}. {error.Trim()}");
                    }

                    return output.Trim();
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Perforce command failed: p4 {arguments}",
                    exception);
            }
        }
    }
}
