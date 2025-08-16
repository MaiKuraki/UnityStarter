using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement
{
	public static class YieldUtil
	{
		public static async Task Next(CancellationToken cancellationToken = default)
		{
			await UniTask.Yield(cancellationToken);
		}
	}
}