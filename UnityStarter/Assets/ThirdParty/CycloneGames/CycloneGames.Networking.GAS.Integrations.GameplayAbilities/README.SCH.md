# CycloneGames.Networking.GAS.Integrations.GameplayAbilities

<div align="left"><a href="./README.md">English</a> | 简体中文</div>

用于桥接 CycloneGames.Networking.GAS 与 CycloneGames.GameplayAbilities 的集成包。

## 本包提供

- GameplayAbilitiesNetworkedASCAdapter:
  - 实现 INetworkedASC。
  - 支持 ASC 完整状态快照采集（用于重连/晚加入）。
  - 支持将复制过来的属性、标签、效果数据应用到本地 ASC。
- IGasNetIdRegistry:
  - 稳定 ID 注册中心（能力/效果/属性/标签 + ASC 与网络实体映射）。
- DefaultGasNetIdRegistry:
  - 默认运行时实现，基于确定性 FNV-1a 哈希和查找表。
- IGasReplicatedEffectMutationHandler:
  - 远端效果移除与堆叠变更的通用策略接口。
- IGasFullStateAuthorizationPolicy + GasBridgeSecurityExtensions:
  - 完整状态请求鉴权的通用策略模型。
- IConnectionRateLimiter + InMemoryTokenBucketRateLimiter:
  - 可复用的连接级请求限频能力。
- IGasSecurityAuditSink + UnityLogGasSecurityAuditSink:
  - 安全审计抽象与默认 Unity 日志实现。

## 重要说明

- AbilitySystemComponent 当前未公开“按远端 effectInstanceId 精确移除/改堆叠”的公共 API。
- 适配器提供了通用策略入口 EffectMutationHandler，以及兼容回调入口 TryRemoveReplicatedEffect / TryApplyReplicatedStackChange，便于你先接入。
- 后续如果你在 ASC 增加显式公共 API，只需替换策略实现，不需要改网络协议层代码。

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
