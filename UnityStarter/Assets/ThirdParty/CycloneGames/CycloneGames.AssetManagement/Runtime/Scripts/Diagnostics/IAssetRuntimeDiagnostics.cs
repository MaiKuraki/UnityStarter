namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Optional package capability for runtime-safe aggregate diagnostics.
    /// Implementations must not allocate per cache entry or expose provider handles.
    /// </summary>
    public interface IAssetRuntimeDiagnostics
    {
        AssetRuntimeCacheSnapshot GetRuntimeCacheSnapshot();
    }
}
