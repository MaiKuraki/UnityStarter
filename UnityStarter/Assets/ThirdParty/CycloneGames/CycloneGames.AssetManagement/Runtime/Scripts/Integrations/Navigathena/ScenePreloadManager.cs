#if NAVIGATHENA_PRESENT && NAVIGATHENA_YOOASSET
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using CycloneGames.AssetManagement.Preload;

namespace CycloneGames.AssetManagement.Integrations.Navigathena
{
	/// <summary>
	/// Preloads/unloads manifests on scene change. Call hooks from Navigathena scene events.
	/// </summary>
	public sealed class ScenePreloadManager
	{
		private readonly IAssetPackage _package;
		private readonly ScenePreloadRegistry _registry;
		private readonly List<PreloadRunner> _active = new List<PreloadRunner>(4);
		private CancellationTokenSource _cts;

		public ScenePreloadManager(IAssetPackage package, ScenePreloadRegistry registry)
		{
			_package = package;
			_registry = registry;
		}

		public async Task OnBeforeLoadSceneAsync(string nextSceneKey)
		{
			CancelAll();
			_active.Clear();
			_cts = new CancellationTokenSource();
			var manifests = _registry?.GetManifests(nextSceneKey);
			if (manifests == null) return;
			foreach (var m in manifests)
			{
				var runnerGo = new GameObject($"[PreloadRunner] {m.ManifestName}");
				Object.DontDestroyOnLoad(runnerGo);
				var runner = runnerGo.AddComponent<PreloadRunner>();
				runner.Manifest = m;
				runner.Package = _package;
				_active.Add(runner);
			}
			// run sequentially to avoid burst thrash; can be parallelized if needed
			foreach (var r in _active)
			{
				await r.RunAsync(_cts.Token);
			}
		}

		public void OnAfterLoadScene(string loadedSceneKey)
		{
			// Optional: keep preloaded assets until a later scope exit
			// Currently, we keep them cached; unloading logic can be added based on policy.
			DisposeRunners();
		}

		public void CancelAll()
		{
			if (_cts != null && !_cts.IsCancellationRequested) _cts.Cancel();
			DisposeRunners();
		}

		private void DisposeRunners()
		{
			for (int i = 0; i < _active.Count; i++)
				if (_active[i] != null) Object.Destroy(_active[i].gameObject);
			_active.Clear();
			_cts?.Dispose();
			_cts = null;
		}
	}
}
#endif