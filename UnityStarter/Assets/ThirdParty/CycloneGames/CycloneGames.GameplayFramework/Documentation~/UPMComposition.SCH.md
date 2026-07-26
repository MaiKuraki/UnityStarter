# GameplayFramework UPM 组合

[English](UPMComposition.md)

## 用途

`com.cyclone-games.gameplay-framework` 将 AssetManagement 与 GameplayAbilities 保持为可选 integration；两者都不是 GameplayFramework package 的必需依赖。

## Assembly 门控

| Assembly | 支持的 UPM package | Capability | Consumer 引用 |
| --- | --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement` | `com.cyclone-games.asset-management` `1.x` | `CYCLONEGAMES_HAS_ASSET_MANAGEMENT` | 显式 |
| `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayAbilities` | `com.cyclone-games.gameplay-abilities` `1.x` | `CYCLONEGAMES_HAS_GAMEPLAY_ABILITIES` | 显式 |

每个 integration asmdef 都通过 `versionDefines` 生成 capability，通过 `defineConstraints` 消费该 capability，并使用 `autoReferenced: false`。请通过 UPM 安装可选 package，只让 consumer 显式引用实际使用的 integration assembly。不要在 PlayerSettings 中添加这些 capability symbol。

可选 package 缺失或不在支持的版本范围内时，Unity 只排除对应 integration；`CycloneGames.GameplayFramework.Runtime` 与 `CycloneGames.GameplayFramework.Networking` 仍然可用。

package-derived version define 要求 package 由 UPM 解析。仅把相邻 package 放在 `Assets/` 下不会激活这些 capability。

## 测试

- AssetManagement integration tests：`CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor`
- GameplayAbilities integration tests：`CycloneGames.GameplayFramework.Integrations.GameplayAbilities.Tests.Editor`
- Core EditMode tests：`CycloneGames.GameplayFramework.Tests.Editor`

Core test assembly 不再引用可选 package。验证时应同时覆盖“依赖缺失”和“依赖存在”两种 UPM profile：缺失时不应编译可选 integration 及其 tests；存在时应运行两个 gated test assembly。

## 持久化

此组合设计不写文件，也不引入 serialized state。移除可选 package 只会移除其 gated assembly，不需要资产或存档迁移。

## 故障排查

| 现象 | 处理 |
| --- | --- |
| 可选 integration assembly 不存在 | 确认 UPM 已解析对应 package 的受支持 `1.x` 版本，再添加显式 consumer asmdef reference。 |
| 移除可选 package 后 core package 失败 | 检查 consumer 与 test asmdef 是否仍直接引用可选 package；GameplayFramework core assembly 不依赖两个 integration。 |
| capability 在不同 checkout 中表现不同 | 比较 `Packages/manifest.json` 与 `Packages/packages-lock.json`；不要依赖 PlayerSettings symbol 或相邻 `Assets/` manifest。 |
