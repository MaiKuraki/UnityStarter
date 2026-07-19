using System.Collections.Generic;
using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime.Batch
{
	public interface IGroupOperation : IOperation
	{
		IReadOnlyList<IOperation> Items { get; }
		void Add(IOperation op, float weight = 1f);
		/// <summary>
		/// Starts or joins the memoized group execution. The passed token cancels only this caller's wait;
		/// <see cref="Cancel"/> owns cancellation of the shared group stage.
		/// </summary>
		UniTask StartAsync(CancellationToken cancellationToken = default);
		void Cancel();
	}
}
