using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.AssetManagement.Preload
{
	public sealed class PreloadRunner : MonoBehaviour
	{
		public PreloadManifest Manifest;
		public IAssetPackage Package;
		public float Progress { get; private set; }
		public bool IsDone { get; private set; }
		public string Error { get; private set; }
		private System.Collections.Generic.List<IAssetHandle<Object>> _retained = new System.Collections.Generic.List<IAssetHandle<Object>>(8);

		public async Task RunAsync(CancellationToken cancellationToken = default)
		{
			IsDone = false; Progress = 0f; Error = null;
			if (Manifest == null || Package == null) { IsDone = true; return; }
			var group = new GroupOperation();
			_retained.Clear();
			for (int i = 0; i < Manifest.Assets.Count; i++)
			{
				var entry = Manifest.Assets[i];
				var op = Package.LoadAssetAsync<Object>(entry.Location); // warm and retain until after scene switch
				group.Add(op, Manifest.UseUniformWeights ? 1f : entry.Weight);
				_retained.Add(op);
			}
			var task = group.StartAsync(cancellationToken);
			while (!group.IsDone)
			{
				Progress = group.Progress;
				await YieldUtil.Next(cancellationToken);
			}
			await task;
			Error = group.Error;
			IsDone = true;
		}

		private void OnDestroy()
		{
			// Release retained handles when runner is destroyed by ScenePreloadManager.OnAfterLoadScene
			for (int i = 0; i < _retained.Count; i++)
			{
				_retained[i]?.Dispose();
			}
			_retained.Clear();
		}
	}
}