# HybridCLR + Obfuz 热更新 Provider

本集成是 HybridCLR hot-update provider 的显式混淆变体。热更新 DLL 必须经过 Obfuz4HybridCLR 时，为 `hot-update` invocation 指定 `HybridCLRObfuzBuildConfig`。标准 `HybridCLRBuildConfig` 绝不会隐式启用该行为。

Adapter 要求兼容的 HybridCLR、Obfuz 与 Obfuz4HybridCLR Editor API，并要求 Obfuz Encryption VM 已编译。Package 缺失或 API 不兼容会在 preflight 阶段失败，但不会给 core pipeline 增加 package 编译期引用。

Clean 模式使用 `../HybridCLR/README.SCH.md` 中说明的同一套事务化 Runtime 输出、recovery journal 与 terminal publication barrier。Incremental 模式会被拒绝，因为当前 Obfuz4HybridCLR API 无法接收显式验证后的 AOT baseline 目录。

修改后应编译两份 Build assembly，运行 `HotUpdateBuildAdapterTests` 与 HybridCLR transaction/baseline EditMode tests，并使用 Release CI 的精确 package 集执行一次目标平台 Clean build。
