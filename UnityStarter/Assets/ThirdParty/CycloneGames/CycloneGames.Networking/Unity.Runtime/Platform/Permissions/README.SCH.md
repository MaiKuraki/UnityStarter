# Network Host Permissions

本目录提供面向 Unity Runtime 的平台辅助能力，用于判断当前设备是否适合作为局域网 Listen Server。API 与具体传输层无关，可以在启动 Mirror、Mirage、Nakama 或自定义 `INetTransport` 之前调用。

## 职责

- 判断当前平台是否适合创建局域网 Listen Server。
- 在 Windows Editor 和 Windows Standalone 中请求 Windows Defender Firewall 入站规则。
- 异步校验 Windows 实时防火墙状态（UniTask + 取消），并回报经验证的结果。
- 枚举本机局域网 IPv4 地址，方便联机菜单展示给玩家。
- 提供可挂到场景或菜单对象上的 `NetworkHostPermissionProbe`。
- 提供 Editor 诊断窗口：`Tools/CycloneGames/Networking/LAN Host Permission`。

## 核心类型

| 类型 | 用途 |
| --- | --- |
| `INetworkHostPermissionService` | Runtime 层 Host 权限服务契约。 |
| `NetworkHostPermissionServiceFactory` | 创建当前平台对应的默认服务。 |
| `WindowsNetworkHostPermissionService` | 通过管理员 PowerShell 请求添加 Windows 入站防火墙规则。 |
| `StaticNetworkHostPermissionService` | 为无法由应用直接修改防火墙的平台返回固定说明。 |
| `NetworkLocalAddressUtility` | 收集本机局域网 IPv4 地址候选。 |
| `NetworkHostPermissionProbe` | 面向场景和菜单的 `MonoBehaviour` 桥接组件。 |

## 平台行为

| 平台 | 可作为 LAN Host | 运行时请求系统权限 | 说明 |
| --- | --- | --- | --- |
| Windows Editor / Standalone | 可以 | 可以 | 用户同意 UAC 后，为当前进程添加指定端口和协议的入站防火墙规则。 |
| macOS Editor / Standalone | 可以 | 不可以 | 依赖系统防火墙弹窗或用户手动管理防火墙设置。 |
| Linux Editor / Standalone | 可以 | 不可以 | 不同发行版防火墙工具不同，应向玩家或现场组织者展示 IP 和端口。 |
| Android | 取决于权限和网络环境 | 不可以 | 需要正确的构建权限和网络环境，无法修改路由器、热点或系统防火墙。 |
| iOS | 取决于权限和网络环境 | 不可以 | 需要配置本地网络权限说明，并由玩家授权，无法修改防火墙设置。 |
| WebGL | 不可以 | 不可以 | WebGL 不能作为普通局域网 Listen Server，也不能接收 UDP Discovery 数据包。 |

## Runtime 使用方式

```csharp
using CycloneGames.Networking.Platform;

INetworkHostPermissionService service = NetworkHostPermissionServiceFactory.CreateDefault("My Game LAN Host");
NetworkHostPermissionCheckResult status = service.GetStatus(7777, NetworkTransportProtocol.Udp);

// status.CanHostLan / status.RequiresSystemConfiguration 表达就绪状态。
// status.DeveloperMessage 仅用于开发者诊断（非本地化的玩家文案）。
if (status.CanRequestAutomatically)
{
    NetworkHostPermissionRequestResult result = service.RequestSystemConfiguration(7777, NetworkTransportProtocol.Udp);
    // result.Launched 为 true 表示系统弹窗已弹出；是否真正成功可通过校验或另一台机器连接验证。
}

// 在 Windows 上不阻塞主线程地校验实时防火墙状态（UniTask + CancellationToken）。
NetworkHostPermissionCheckResult verified = await service.RefreshStatusAsync(7777, NetworkTransportProtocol.Udp, ct);
if (verified.IsVerified && !verified.RequiresSystemConfiguration)
{
    // 该端口/协议的入站防火墙规则已启用；LAN 对端可以连到本主机。
}
```

```csharp
using System.Collections.Generic;
using CycloneGames.Networking.Platform;

List<string> addresses = new List<string>();
NetworkLocalAddressUtility.GetLanIPv4Addresses(addresses);
```

## 可扩展性

`NetworkHostPermissionServiceFactory.CreateDefault` 返回的只是平台默认基线。对于该模块无法直接表达的真实
能力（例如 iOS 原生 Local Network 权限触发、Android nearby/Wi-Fi 权限流程、或调用发行版防火墙工具的
Linux 服务），请实现 `INetworkHostPermissionService` 并在使用处注入：

```csharp
var probe = GetComponent<NetworkHostPermissionProbe>();
probe.SetPermissionService(new MyNativeIosLocalNetworkService());
```

工厂不持有任何全局可变状态，因此自定义行为是逐调用点 opt-in 的，且测试隔离。

## 持久化行为

本模块不会写入 ProjectSettings、PlayerPrefs、EditorPrefs、存档文件或隐藏全局状态。

在 Windows 上，如果用户同意授权，会创建或替换一条系统防火墙规则，命名格式如下：

```text
<RuleDisplayNamePrefix> <PROTOCOL> <PORT>
```

这条规则归 Windows Defender Firewall 管理，可以从 Windows 设置或 PowerShell 中删除。

## 验证步骤

1. 在 Unity Editor 中打开 `Tools/CycloneGames/Networking/LAN Host Permission`。
2. 设置所选 transport 实际使用的协议和端口。
3. 确认窗口中至少显示一个局域网 IPv4 地址。
4. Windows 平台点击 `Request Firewall Rule`，并同意 UAC 弹窗。
5. 点击 `Verify Firewall Rule`；状态应报告为已验证的规则（无需再配置）。
6. 从同一局域网中的另一台机器连接窗口列出的 Host IP 和端口。
7. 构建 Windows Standalone 后，在打包版本中再次请求一次防火墙授权，让规则指向玩家可执行文件，而不是 Unity Editor 可执行文件。
