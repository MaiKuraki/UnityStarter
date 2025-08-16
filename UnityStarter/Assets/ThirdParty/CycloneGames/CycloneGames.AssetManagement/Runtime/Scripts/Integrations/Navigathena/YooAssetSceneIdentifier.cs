#if NAVIGATHENA_PRESENT && NAVIGATHENA_YOOASSET
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using NaviSM = MackySoft.Navigathena.SceneManagement;

namespace CycloneGames.AssetManagement.Integrations.Navigathena
{
	/// <summary>
	/// Navigathena-compatible scene identifier backed by CycloneGames.AssetManagement (YooAsset provider by default).
	/// Keep Addressables keys identical to YooAsset locations for transparent switching.
	/// </summary>
    public sealed class YooAssetSceneIdentifier : NaviSM.ISceneIdentifier
	{
		private readonly IAssetPackage _package;
		private readonly string _location;
		private readonly LoadSceneMode _mode;
		private readonly bool _activateOnLoad;
		private readonly int _priority;

		public YooAssetSceneIdentifier(IAssetPackage package, string location, LoadSceneMode mode = LoadSceneMode.Additive, bool activateOnLoad = true, int priority = 100)
		{
			_package = package ?? throw new ArgumentNullException(nameof(package));
			_location = string.IsNullOrEmpty(location) ? throw new ArgumentNullException(nameof(location)) : location;
			_mode = mode;
			_activateOnLoad = activateOnLoad;
			_priority = priority;
		}

        public NaviSM.ISceneHandle CreateHandle()
		{
			return new Handle(_package, _location, _mode, _activateOnLoad, _priority);
		}

        private sealed class Handle : NaviSM.ISceneHandle
		{
			private readonly IAssetPackage _package;
			private readonly string _location;
			private readonly LoadSceneMode _mode;
			private readonly bool _activateOnLoad;
			private readonly int _priority;
			private CycloneGames.AssetManagement.ISceneHandle _yooHandle;

			public Handle(IAssetPackage package, string location, LoadSceneMode mode, bool activateOnLoad, int priority)
			{
				_package = package;
				_location = location;
				_mode = mode;
				_activateOnLoad = activateOnLoad;
				_priority = priority;
			}

			public async UniTask<Scene> Load(IProgress<float> progress = null, CancellationToken cancellationToken = default)
			{
				_yooHandle = _package.LoadSceneAsync(_location, _mode, _activateOnLoad, _priority);
				while (!_yooHandle.IsDone)
				{
					progress?.Report(_yooHandle.Progress);
					if (cancellationToken.IsCancellationRequested)
						throw new OperationCanceledException(cancellationToken);
					await global::CycloneGames.AssetManagement.YieldUtil.Next(cancellationToken);
				}

				// Resolve Scene by name from handle
				Scene scene = SceneManager.GetSceneByName(_yooHandle.ScenePath);
				return scene;
			}

			public async UniTask Unload(IProgress<float> progress = null, CancellationToken cancellationToken = default)
			{
				if (_yooHandle == null) return;
				await _package.UnloadSceneAsync(_yooHandle);
				_yooHandle = null;
			}
		}
	}
}

#endif