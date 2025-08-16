using System;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.AssetManagement.Retry
{
	public sealed class RetryPolicy
	{
		public int MaxAttempts { get; }
		public TimeSpan InitialDelay { get; }
		public double BackoffFactor { get; }

		public RetryPolicy(int maxAttempts = 3, double initialDelaySeconds = 0.5, double backoffFactor = 2.0)
		{
			MaxAttempts = Math.Max(1, maxAttempts);
			InitialDelay = TimeSpan.FromSeconds(Math.Max(0, initialDelaySeconds));
			BackoffFactor = Math.Max(1, backoffFactor);
		}

		public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, Func<Exception, bool> shouldRetry, CancellationToken ct)
		{
			TimeSpan delay = InitialDelay;
			Exception last = null;
			for (int attempt = 1; attempt <= MaxAttempts; attempt++)
			{
				try { return await action(); }
				catch (Exception ex) when (shouldRetry?.Invoke(ex) == true && attempt < MaxAttempts)
				{
					if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
					delay = TimeSpan.FromSeconds(delay.TotalSeconds * BackoffFactor);
					last = ex;
				}
				catch (Exception ex)
				{
					last = ex;
					break;
				}
			}
			throw last ?? new Exception("RetryPolicy: Unknown failure without exception.");
		}
	}
}