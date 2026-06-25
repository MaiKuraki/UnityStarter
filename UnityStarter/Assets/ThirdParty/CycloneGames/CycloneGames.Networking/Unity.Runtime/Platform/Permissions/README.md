# Network Host Permissions

This folder contains Unity-facing platform helpers for LAN listen-server readiness. The API is transport-neutral and can be used before starting Mirror, Mirage, Nakama, or a custom `INetTransport` implementation.

## Responsibilities

- Report whether the current platform can reasonably host a LAN listen-server.
- Request a Windows Defender Firewall inbound rule in Windows Editor and Windows standalone builds.
- Verify the live Windows firewall state asynchronously (UniTask + cancellation) and report a confirmed result.
- Enumerate local IPv4 address candidates for LAN room UI.
- Provide `NetworkHostPermissionProbe` for scene and menu integration.
- Provide an Editor diagnostic window through `Tools/CycloneGames/Networking/LAN Host Permission`.

## Core Types

| Type | Purpose |
| --- | --- |
| `INetworkHostPermissionService` | Runtime-facing host permission service contract. |
| `NetworkHostPermissionServiceFactory` | Creates the platform-specific default service. |
| `WindowsNetworkHostPermissionService` | Opens an elevated Windows PowerShell request to add an inbound firewall rule. |
| `StaticNetworkHostPermissionService` | Returns static platform guidance for platforms where the app cannot edit firewall settings. |
| `NetworkLocalAddressUtility` | Collects local LAN IPv4 address candidates. |
| `NetworkHostPermissionProbe` | Optional `MonoBehaviour` bridge for scenes and menus. |

## Platform Behavior

| Platform | Host LAN | Runtime Permission Request | Notes |
| --- | --- | --- | --- |
| Windows Editor / Standalone | Yes | Yes | Adds a port/protocol inbound firewall rule for the current process after UAC approval. |
| macOS Editor / Standalone | Yes | No | Rely on the system firewall prompt or user-managed firewall settings. |
| Linux Editor / Standalone | Yes | No | Firewall tooling varies by distribution. Show IP and port to the player or event organizer. |
| Android | Possible | No | Requires the correct build permissions and network environment. Cannot edit router or system firewall settings. |
| iOS | Possible | No | Requires local network permission configuration and player approval. Cannot edit firewall settings. |
| WebGL | No | No | WebGL cannot host a normal LAN listen-server or receive UDP discovery packets. |

## Runtime Usage

```csharp
using CycloneGames.Networking.Platform;

INetworkHostPermissionService service = NetworkHostPermissionServiceFactory.CreateDefault("My Game LAN Host");
NetworkHostPermissionCheckResult status = service.GetStatus(7777, NetworkTransportProtocol.Udp);

// status.CanHostLan and status.RequiresSystemConfiguration describe readiness.
// status.DeveloperMessage is developer guidance only (not localized player copy).
if (status.CanRequestAutomatically)
{
    NetworkHostPermissionRequestResult result = service.RequestSystemConfiguration(7777, NetworkTransportProtocol.Udp);
    // result.Launched means the OS prompt was shown; confirm by verifying or connecting from another peer.
}

// On Windows, verify the live firewall state without blocking the main thread (UniTask + CancellationToken).
NetworkHostPermissionCheckResult verified = await service.RefreshStatusAsync(7777, NetworkTransportProtocol.Udp, ct);
if (verified.IsVerified && !verified.RequiresSystemConfiguration)
{
    // An enabled inbound firewall rule for this port/protocol is present; LAN peers can reach the host.
}
```

```csharp
using System.Collections.Generic;
using CycloneGames.Networking.Platform;

List<string> addresses = new List<string>();
NetworkLocalAddressUtility.GetLanIPv4Addresses(addresses);
```

## Extensibility

The platform default from `NetworkHostPermissionServiceFactory.CreateDefault` is only a baseline. Implement
`INetworkHostPermissionService` to add real behavior a platform cannot express here — for example a native iOS
Local Network permission trigger, an Android nearby/Wi-Fi permission flow, or a Linux service that shells out to
the distribution firewall — and inject it where it is used:

```csharp
var probe = GetComponent<NetworkHostPermissionProbe>();
probe.SetPermissionService(new MyNativeIosLocalNetworkService());
```

The factory holds no global mutable state, so custom behavior is opt-in per call site and stays test-isolated.

## Persistence

This module does not write project settings, player preferences, editor preferences, save files, or hidden global state.

On Windows, a successful permission request creates or replaces an operating system firewall rule named with this pattern:

```text
<RuleDisplayNamePrefix> <PROTOCOL> <PORT>
```

The rule is owned by Windows Defender Firewall and can be removed from Windows settings or PowerShell.

## Validation

1. Open `Tools/CycloneGames/Networking/LAN Host Permission` in the Unity Editor.
2. Set the protocol and port used by the selected transport.
3. Confirm the window lists at least one LAN IPv4 address.
4. On Windows, click `Request Firewall Rule` and approve the UAC prompt.
5. Click `Verify Firewall Rule`; confirm the status reports a verified rule with no further configuration required.
6. From another machine on the same LAN, connect to the listed host IP and port.
7. Build a Windows standalone player and repeat the firewall request from the build so the rule targets the player executable rather than the Unity Editor executable.
