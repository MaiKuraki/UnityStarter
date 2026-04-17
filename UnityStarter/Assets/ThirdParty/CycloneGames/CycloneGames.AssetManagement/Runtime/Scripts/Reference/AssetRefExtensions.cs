using System.Threading;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Extension methods that bridge <see cref="AssetRef{T}"/>, <see cref="AssetRef"/>, and <see cref="SceneRef"/>
    /// to <see cref="IAssetPackage"/> loading APIs.
    /// <para>
    /// These keep AssetRef as a pure data key while providing ergonomic loading syntax:
    /// <code>IAssetHandle<Sprite> handle = package.LoadAsync(myAssetRef);</code>
    /// </para>
    /// </summary>
    public static class AssetRefExtensions
    {
        // ── AssetRef<T> ──────────────────────────────────────────────────────────

        public static IAssetHandle<T> LoadAsync<T>(
            this IAssetPackage package,
            AssetRef<T> assetRef,
            string bucket = null,
            string tag = null,
            string owner = null,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            return package.LoadAssetAsync<T>(assetRef.Location, bucket, tag, owner, cancellationToken);
        }

        public static IAssetHandle<T> LoadSync<T>(
            this IAssetPackage package,
            AssetRef<T> assetRef,
            string bucket = null,
            string tag = null,
            string owner = null) where T : UnityEngine.Object
        {
            return package.LoadAssetSync<T>(assetRef.Location, bucket, tag, owner);
        }

        public static IAssetHandle<T> LoadAsync<T>(
            this AssetBucketScope scope,
            AssetRef<T> assetRef,
            string tag = null,
            string owner = null,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            return scope.LoadAssetAsync<T>(assetRef.Location, tag, owner, cancellationToken);
        }

        public static IAssetHandle<T> LoadSync<T>(
            this AssetBucketScope scope,
            AssetRef<T> assetRef,
            string tag = null,
            string owner = null) where T : UnityEngine.Object
        {
            return scope.LoadAssetSync<T>(assetRef.Location, tag, owner);
        }

        // ── AssetRef (non-generic) ──────────────────────────────────────────────

        public static IAssetHandle<T> LoadAsync<T>(
            this IAssetPackage package,
            AssetRef assetRef,
            string bucket = null,
            string tag = null,
            string owner = null,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            return package.LoadAssetAsync<T>(assetRef.Location, bucket, tag, owner, cancellationToken);
        }

        public static IAssetHandle<T> LoadSync<T>(
            this IAssetPackage package,
            AssetRef assetRef,
            string bucket = null,
            string tag = null,
            string owner = null) where T : UnityEngine.Object
        {
            return package.LoadAssetSync<T>(assetRef.Location, bucket, tag, owner);
        }

        public static IAssetHandle<T> LoadAsync<T>(
            this AssetBucketScope scope,
            AssetRef assetRef,
            string tag = null,
            string owner = null,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            return scope.LoadAssetAsync<T>(assetRef.Location, tag, owner, cancellationToken);
        }

        public static IAssetHandle<T> LoadSync<T>(
            this AssetBucketScope scope,
            AssetRef assetRef,
            string tag = null,
            string owner = null) where T : UnityEngine.Object
        {
            return scope.LoadAssetSync<T>(assetRef.Location, tag, owner);
        }

        // ── SceneRef ────────────────────────────────────────────────────────────

        public static ISceneHandle LoadSceneAsync(
            this IAssetPackage package,
            SceneRef sceneRef,
            LoadSceneMode loadMode = LoadSceneMode.Single,
            bool activateOnLoad = true,
            int priority = 100,
            string bucket = null)
        {
            return package.LoadSceneAsync(sceneRef.Location, loadMode, activateOnLoad, priority, bucket);
        }

        public static ISceneHandle LoadSceneSync(
            this IAssetPackage package,
            SceneRef sceneRef,
            LoadSceneMode loadMode = LoadSceneMode.Single,
            string bucket = null)
        {
            return package.LoadSceneSync(sceneRef.Location, loadMode, bucket);
        }

        public static ISceneHandle LoadSceneAsync(
            this AssetBucketScope scope,
            SceneRef sceneRef,
            LoadSceneMode loadMode = LoadSceneMode.Single,
            bool activateOnLoad = true,
            int priority = 100)
        {
            return scope.LoadSceneAsync(sceneRef.Location, loadMode, activateOnLoad, priority);
        }

        public static ISceneHandle LoadSceneSync(
            this AssetBucketScope scope,
            SceneRef sceneRef,
            LoadSceneMode loadMode = LoadSceneMode.Single)
        {
            return scope.LoadSceneSync(sceneRef.Location, loadMode);
        }
    }
}
