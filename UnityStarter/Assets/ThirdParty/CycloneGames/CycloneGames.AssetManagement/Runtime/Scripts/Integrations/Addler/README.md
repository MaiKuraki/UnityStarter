# Optional: Addler Adapter (ADDLER_PRESENT)

- Register Addler-derived keys to YooAsset locations at boot:

```csharp
#if ADDLER_PRESENT
using CycloneGames.AssetManagement.Integrations.Addler;

var resolver = new AddlerAssetKeyResolver();
// Example: map Addler label or GUID to a YooAsset location
resolver.Register("UI:MainMenu", "Assets/UI/Prefabs/MainMenu.prefab");
resolver.Freeze();

// Usage in your service
if (resolver.TryResolve("UI:MainMenu", out var loc))
{
    var handle = pkg.LoadAssetSync<UnityEngine.GameObject>(loc);
    var go = pkg.InstantiateSync(handle);
}
#endif
```

- Design notes:

- Read-only after Freeze() for thread-safety and zero-alloc lookups
- No Addler runtime dependency beyond compile-time symbol
