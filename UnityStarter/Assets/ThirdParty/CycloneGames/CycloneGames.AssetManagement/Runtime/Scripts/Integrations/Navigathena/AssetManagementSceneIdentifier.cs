#if NAVIGATHENA_PRESENT
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.SceneManagement;

namespace CycloneGames.AssetManagement.Runtime.Integrations.Navigathena
{
    /// <summary>
    /// A provider-agnostic scene identifier for Navigathena that uses the IAssetPackage abstraction.
    /// </summary>
    public class AssetManagementSceneIdentifier : ISceneIdentifier
    {
        private readonly IAssetPackage assetPackage;
        private readonly string location;
        private readonly UnityEngine.SceneManagement.LoadSceneMode loadSceneMode;
        private readonly bool activateOnLoad;

        public AssetManagementSceneIdentifier(IAssetPackage assetPackage, string location, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode = UnityEngine.SceneManagement.LoadSceneMode.Single, bool activateOnLoad = true)
        {
            this.assetPackage = assetPackage;
            this.location = location;
            this.loadSceneMode = loadSceneMode;
            this.activateOnLoad = activateOnLoad;
        }

        public MackySoft.Navigathena.SceneManagement.ISceneHandle CreateHandle()
        {
            var sceneHandle = assetPackage.LoadSceneAsync(location, loadSceneMode, activateOnLoad);
            return new NavigathenaSceneHandleAdapter(sceneHandle, assetPackage);
        }
    }

    /// <summary>
    /// Adapts a CycloneGames.AssetManagement.ISceneHandle to the MackySoft.Navigathena.SceneManagement.ISceneHandle interface.
    /// </summary>
    public class NavigathenaSceneHandleAdapter : MackySoft.Navigathena.SceneManagement.ISceneHandle
    {
        private readonly ISceneHandle sceneHandle;
        private readonly IAssetPackage assetPackage;

        public UnityEngine.SceneManagement.Scene Scene => sceneHandle.Scene;

        public NavigathenaSceneHandleAdapter(ISceneHandle inSceneHandle, IAssetPackage assetPackage)
        {
            this.sceneHandle = inSceneHandle;
            this.assetPackage = assetPackage;
        }

        public async UniTask<UnityEngine.SceneManagement.Scene> Load(IProgress<float> progress = null, CancellationToken cancellationToken = default)
        {
            while (!sceneHandle.IsDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(sceneHandle.Progress);
                await UniTask.Yield(cancellationToken);
            }
            
            return sceneHandle.Scene;
        }

        public UniTask Unload(IProgress<float> progress = null, CancellationToken cancellationToken = default)
        {
            return assetPackage.UnloadSceneAsync(sceneHandle);
        }

        public void Dispose()
        {
            (sceneHandle as IDisposable)?.Dispose();
        }
    }
}
#endif