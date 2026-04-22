# CycloneGames.Networking.GAS.Integrations.GameplayAbilities

[English](./README.md) | 简体中文

用于桥接 CycloneGames.Networking.GAS 与 CycloneGames.GameplayAbilities 的集成包。

## 本包提供

- GameplayAbilitiesNetworkedASCAdapter:
  - 实现 INetworkedASC。
  - 支持 ASC 完整状态快照采集（用于重连/晚加入）。
  - 支持细化后的 effect 增量采集，区分 add/update/stack-change/remove。
  - 支持将复制过来的属性、标签、效果数据应用到本地 ASC。
- IGasNetIdRegistry:
  - 稳定 ID 注册中心（能力/效果/属性/标签 + ASC 与网络实体映射）。
- DefaultGasNetIdRegistry:
  - 默认运行时实现，基于确定性 FNV-1a 哈希和查找表。
- IGasReplicatedEffectMutationHandler:
  - 远端效果移除、堆叠变更与原地更新的通用策略接口。
- GasBridgeGameplayAbilitiesExtensions:
  - 一行完成 GameplayAbilitiesNetworkedASCAdapter 的 bridge 注册。
  - 一行完成基于 observer 解析器的 pending delta 复制。
  - 一行完成断线重连 / 晚加入的 full-state 下发。
- IGasFullStateAuthorizationPolicy + GasBridgeSecurityExtensions:
  - 完整状态请求鉴权的通用策略模型。
- IConnectionRateLimiter + InMemoryTokenBucketRateLimiter:
  - 可复用的连接级请求限频能力。
- IGasSecurityAuditSink + UnityLogGasSecurityAuditSink:
  - 安全审计抽象与默认 Unity 日志实现。

## 重要说明

- Adapter 现在已经有内建 fallback，可通过 CycloneGames.GameplayAbilities 中新增的 ASC 公共变更 API 直接处理 replicated effect 的 remove / stack / update。
- EffectMutationHandler 以及回调入口 TryRemoveReplicatedEffect / TryApplyReplicatedStackChange / TryApplyReplicatedEffectUpdate 仍然保留，适用于需要更严格权限控制或自定义副作用的项目。
- GAS 对外时间接口仍保持 float，这样更符合 Unity 使用习惯，也更贴近 UE 风格的 authoring / inspector 体验；如果担心精度问题，优先通过权威快照 / delta 同步解决，而不是把整套作者接口扩成 double。

## 主流程接线示例

```csharp
var adapter = bridge.RegisterGameplayAbilitiesASC(
  asc,
  networkId,
  ownerConnectionId,
  idRegistry);

bridge.ReplicatePendingState(adapter, GetObservers);

// 断线重连或晚加入
bridge.SendGameplayAbilitiesFullState(adapter, clientConnection);
```

## 通用策略接线示例

```csharp
var limiter = new InMemoryTokenBucketRateLimiter(capacity: 2f, refillPerSecond: 0.5f);
var auditSink = UnityLogGasSecurityAuditSink.Instance;
var policy = new OwnerOrObserverWithRateLimitPolicy(limiter, auditSink);

bridge.ConfigureFullStateAuthorization(
    policy,
    getOwnerConnectionId: GetOwnerConnectionId,
    getObservers: GetObservers);
```
