using System;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.AssetManagement.Retry
{
	public static class LoadWithRetryExtensions
	{
		public static async Task<IAssetHandle<T>> LoadAssetWithRetryAsync<T>(this IAssetPackage pkg, string location, RetryPolicy policy, CancellationToken ct) where T : UnityEngine.Object
		{
			if (pkg == null) throw new ArgumentNullException(nameof(pkg));
			if (policy == null) throw new ArgumentNullException(nameof(policy));
			return await policy.ExecuteAsync(async () =>
			{
				var h = pkg.LoadAssetAsync<T>(location);
				while (!h.IsDone)
				{
					ct.ThrowIfCancellationRequested();
					await YieldUtil.Next(ct);
				}
				if (!string.IsNullOrEmpty(h.Error)) throw new Exception(h.Error);
				return h;
			}, _ => true, ct);
		}
	}
}