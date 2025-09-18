using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.AssetManagement.Runtime.Batch
{
	public interface IGroupOperation : IOperation
	{
		IReadOnlyList<IOperation> Items { get; }
		void Add(IOperation op, float weight = 1f);
		UniTask StartAsync(CancellationToken cancellationToken = default);
		void Cancel();
	}
}
