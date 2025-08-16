using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.AssetManagement
{
	public interface IGroupOperation : IOperation
	{
		IReadOnlyList<IOperation> Items { get; }
		void Add(IOperation op, float weight = 1f);
		Task StartAsync(CancellationToken cancellationToken = default);
		void Cancel();
	}
}