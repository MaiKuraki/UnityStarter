using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.AssetManagement.Runtime.Batch;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime.Preload
{
	public sealed class PreloadRunner : MonoBehaviour
	{
		public PreloadManifest Manifest;
		public IAssetPackage Package;
		public float Progress { get; private set; }
		public bool IsDone { get; private set; }
		public string Error { get; private set; }
		private readonly List<IAssetHandle<Object>> _retained = new List<IAssetHandle<Object>>(8);
		private CancellationTokenSource _destroyCts;

		/// <summary>
		/// Maximum number of handles to dispose per frame during cleanup.
		/// Prevents frame spikes when releasing large preload batches.
		/// </summary>
		private const int RELEASE_BATCH_SIZE = 8;

		public async UniTask RunAsync(CancellationToken cancellationToken = default)
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

			// Run progress updates in the background without blocking the main await.
			UpdateProgress(group, cancellationToken).Forget();

			await group.StartAsync(cancellationToken);

			Error = group.Error;
			IsDone = true;
		}

		private async UniTaskVoid UpdateProgress(IGroupOperation group, CancellationToken cancellationToken)
		{
			while (!group.IsDone && !cancellationToken.IsCancellationRequested)
			{
				Progress = group.Progress;
				await UniTask.Yield(cancellationToken);
			}
			// Ensure progress is set to 100% when done.
			if (group.IsDone) Progress = 1f;
		}

		private void OnDestroy()
		{
			_destroyCts?.Cancel();
			_destroyCts?.Dispose();
			_destroyCts = null;

			// Release remaining handles synchronously on destroy.
			// Most handles should already be released by ReleaseRetainedAsync;
			// this is a safety net for any stragglers.
			for (int i = 0; i < _retained.Count; i++)
			{
				_retained[i]?.Dispose();
			}
			_retained.Clear();
		}

		/// <summary>
		/// Releases retained handles spread across multiple frames to avoid frame spikes.
		/// Call this instead of waiting for OnDestroy when you want amortized cleanup
		/// (e.g. after the loading screen is shown but before the runner is destroyed).
		/// </summary>
		public async UniTask ReleaseRetainedAsync(CancellationToken cancellationToken = default)
		{
			if (_retained.Count == 0) return;

			_destroyCts?.Cancel();
			_destroyCts?.Dispose();
			_destroyCts = new CancellationTokenSource();

			var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _destroyCts.Token);
			try
			{
				int count = 0;
				for (int i = _retained.Count - 1; i >= 0; i--)
				{
					_retained[i]?.Dispose();
					_retained[i] = null;
					count++;

					if (count >= RELEASE_BATCH_SIZE)
					{
						count = 0;
						await UniTask.Yield(linked.Token);
					}
				}
				_retained.Clear();
			}
			catch (System.OperationCanceledException) { /* OnDestroy will clean up */ }
			finally
			{
				linked.Dispose();
			}
		}
	}
}