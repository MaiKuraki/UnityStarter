#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Networking.Platform
{
    public sealed class WindowsNetworkHostPermissionService : INetworkHostPermissionService
    {
        private readonly string _displayNamePrefix;

        public WindowsNetworkHostPermissionService(string displayNamePrefix)
        {
            _displayNamePrefix = string.IsNullOrWhiteSpace(displayNamePrefix)
                ? NetworkHostPermissionServiceFactory.DEFAULT_RULE_DISPLAY_NAME_PREFIX
                : displayNamePrefix.Trim();
        }

        public NetworkHostPermissionCheckResult GetStatus(int port, NetworkTransportProtocol protocol)
        {
            if (!NetworkPortUtility.IsValidPort(port))
            {
                return NetworkPortUtility.CreateInvalidPortResult(port, "Windows");
            }

            return new NetworkHostPermissionCheckResult(
                NetworkHostPermissionStatus.CanHost,
                true,
                true,
                "Windows",
                "Windows can host LAN sessions. If other players cannot connect, allow this app through Windows Defender Firewall for the selected port and protocol; the automated request below adds that inbound rule.");
        }

        public NetworkHostPermissionRequestResult RequestSystemConfiguration(int port, NetworkTransportProtocol protocol)
        {
            if (!NetworkPortUtility.IsValidPort(port))
            {
                return NetworkPortUtility.CreateInvalidPortRequestResult(port);
            }

            string executablePath = GetCurrentExecutablePath();
            string command = CreatePowerShellCommand(port, protocol, executablePath);

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + QuoteForCommandLine(command),
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(startInfo);
                return new NetworkHostPermissionRequestResult(
                    NetworkHostPermissionRequestOutcome.Launched,
                    "Firewall rule request launched. Approve the Windows prompt, then confirm by connecting from another machine on the same LAN.");
            }
            catch (Exception exception)
            {
                return new NetworkHostPermissionRequestResult(
                    NetworkHostPermissionRequestOutcome.Failed,
                    exception.Message);
            }
        }

        public async UniTask<NetworkHostPermissionCheckResult> RefreshStatusAsync(
            int port,
            NetworkTransportProtocol protocol,
            CancellationToken cancellationToken = default)
        {
            if (!NetworkPortUtility.IsValidPort(port))
            {
                return NetworkPortUtility.CreateInvalidPortResult(port, "Windows");
            }

            string ruleName = CreateRuleName(port, protocol);
            string query = CreateQueryCommand(ruleName);
            string output = await RunQueryAsync(query, cancellationToken);

            bool active = output.IndexOf("ACTIVE", StringComparison.OrdinalIgnoreCase) >= 0;
            string message = active
                ? "Verified: an enabled inbound firewall rule for this port and protocol is present. LAN peers should be able to reach this host."
                : "No enabled inbound firewall rule was found for this port and protocol. Use the automated request, then verify again.";

            return new NetworkHostPermissionCheckResult(
                NetworkHostPermissionStatus.CanHost,
                requiresSystemConfiguration: !active,
                canRequestAutomatically: true,
                isVerified: true,
                platformName: "Windows",
                developerMessage: message);
        }

        private string CreateRuleName(int port, NetworkTransportProtocol protocol)
        {
            string protocolName = protocol == NetworkTransportProtocol.Udp ? "UDP" : "TCP";
            return $"{_displayNamePrefix} {protocolName} {port}";
        }

        private string CreatePowerShellCommand(int port, NetworkTransportProtocol protocol, string executablePath)
        {
            string protocolName = protocol == NetworkTransportProtocol.Udp ? "UDP" : "TCP";
            string ruleName = CreateRuleName(port, protocol);

            StringBuilder builder = new StringBuilder(512);
            builder.Append("$ErrorActionPreference = 'Stop'; ");
            builder.Append("$ruleName = ").Append(ToPowerShellSingleQuotedString(ruleName)).Append("; ");
            builder.Append("Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue; ");
            builder.Append("New-NetFirewallRule ");
            builder.Append("-DisplayName $ruleName ");
            builder.Append("-Direction Inbound ");
            builder.Append("-Action Allow ");
            builder.Append("-Enabled True ");
            builder.Append("-Profile Any ");
            builder.Append("-Protocol ").Append(protocolName).Append(' ');
            builder.Append("-LocalPort ").Append(port).Append(' ');

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                builder.Append("-Program ").Append(ToPowerShellSingleQuotedString(executablePath)).Append(' ');
            }

            builder.Append("| Out-Null");
            return builder.ToString();
        }

        private string CreateQueryCommand(string ruleName)
        {
            // Non-elevated read of the live firewall state; emits a single tiny token so stdout is safe to read.
            StringBuilder builder = new StringBuilder(256);
            builder.Append("$ErrorActionPreference = 'SilentlyContinue'; ");
            builder.Append("$ruleName = ").Append(ToPowerShellSingleQuotedString(ruleName)).Append("; ");
            builder.Append("$rules = Get-NetFirewallRule -DisplayName $ruleName; ");
            builder.Append("if ($rules | Where-Object { $_.Enabled -eq 'True' -and $_.Direction -eq 'Inbound' -and $_.Action -eq 'Allow' }) { 'ACTIVE' } else { 'MISSING' }");
            return builder.ToString();
        }

        private async UniTask<string> RunQueryAsync(string command, CancellationToken cancellationToken)
        {
            string arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + QuoteForCommandLine(command);

            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process process = Process.Start(startInfo))
                    {
                        if (process == null)
                        {
                            return string.Empty;
                        }

                        using (cancellationToken.Register(() => TryKill(process)))
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            process.WaitForExit();
                            return output;
                        }
                    }
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }, cancellationToken: cancellationToken);
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
            }
        }

        private static string GetCurrentExecutablePath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ToPowerShellSingleQuotedString(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }

        private static string QuoteForCommandLine(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }
}
#endif
