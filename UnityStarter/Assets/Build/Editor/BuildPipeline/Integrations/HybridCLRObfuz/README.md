# HybridCLR + Obfuz Hot-Update Provider

This integration is the explicit obfuscated variant of the HybridCLR hot-update provider. Assign `HybridCLRObfuzBuildConfig` to a `hot-update` invocation when hot-update DLLs must pass through Obfuz4HybridCLR. Standard `HybridCLRBuildConfig` never enables this behavior implicitly.

The adapter requires compatible HybridCLR, Obfuz, and Obfuz4HybridCLR editor APIs plus a compiled Obfuz Encryption VM. Missing or incompatible packages fail preflight without adding compile-time package references to the core pipeline.

Clean mode uses the same transactional runtime outputs, recovery journals, and terminal publication barrier documented in `../HybridCLR/README.md`. Incremental mode is rejected because the current Obfuz4HybridCLR API cannot consume the explicit validated AOT baseline directory.

Validate changes by compiling both Build assemblies, running `HotUpdateBuildAdapterTests` and the HybridCLR transaction/baseline EditMode tests, and performing a Clean target-platform build with the exact package set used in release CI.
