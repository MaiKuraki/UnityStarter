using System;
using CycloneGames.AIEnvQuery.Core;
using CycloneGames.BehaviorTree.Core;

namespace CycloneGames.BehaviorTree.Runtime.Integrations.EnvQuery
{
    /// <summary>
    /// Runs an environment query and publishes the best candidate into blackboard slots.
    /// </summary>
    /// <remarks>
    /// The bridge owns the runner and both buffers. <see cref="Run"/> does not allocate, its result is
    /// valid until the next call, and it must run on the creating thread.
    ///
    /// Ordering comes from the runner: score descending, ties by candidate index.
    /// </remarks>
    public sealed class EnvQueryBlackboardBridge : IDisposable
    {
        private static readonly EnvQueryPoint[] NoPoints = Array.Empty<EnvQueryPoint>();

        private readonly IEnvQueryGenerator _generator;
        private readonly IEnvQueryTest[] _tests;
        private readonly IEnvQueryFilter[] _filters;
        private readonly EnvQueryRunner _runner;
        private readonly int _ownerThreadId;
        private EnvQueryPoint[] _results;
        private int _count;
        private bool _disposed;

        /// <param name="generator">Candidate source. The caller owns it.</param>
        /// <param name="tests">Scoring passes; may be null or empty for a pure generator run.</param>
        /// <param name="maxCandidates">Upper bound on candidates per run; sets the runner's buffers.</param>
        /// <param name="resultCapacity">How many ranked candidates are kept and published.</param>
        /// <param name="filters">Rejects candidates before scoring; may be null.</param>
        public EnvQueryBlackboardBridge(
            IEnvQueryGenerator generator,
            IEnvQueryTest[] tests,
            int maxCandidates = 64,
            int resultCapacity = 4,
            IEnvQueryFilter[] filters = null)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            if (maxCandidates < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCandidates), maxCandidates, "At least one candidate must be allowed.");
            }

            if (resultCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(resultCapacity), resultCapacity, "At least one result must be kept.");
            }

            _tests = tests;
            _filters = filters;
            _runner = new EnvQueryRunner(maxCandidates);
            _results = new EnvQueryPoint[resultCapacity];
            _ownerThreadId = Environment.CurrentManagedThreadId;
        }

        public int Capacity => _results.Length;

        /// <summary>Ranked candidates kept by the last run.</summary>
        public int Count => _count;

        public bool HasResult => _count > 0;

        /// <summary>Best candidate of the last run, or a zero point when nothing was generated.</summary>
        public EnvQueryPoint Best => _count > 0 ? _results[0] : default;

        /// <summary>Ranked candidates of the last run, best first.</summary>
        public ReadOnlySpan<EnvQueryPoint> Results => new ReadOnlySpan<EnvQueryPoint>(_results, 0, _count);

        /// <summary>
        /// Executes the query and keeps the best <see cref="Capacity"/> candidates.
        /// </summary>
        /// <returns>Number of ranked candidates kept.</returns>
        public int Run(in EnvQueryContext context)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();

            _count = _runner.Run(_generator, _filters, _tests, in context, _results);
            return _count;
        }

        /// <summary>
        /// Writes whether a candidate was found and the score of the best one. An empty run writes
        /// false and a zero score.
        /// </summary>
        public void PublishBest(RuntimeBlackboard blackboard, int foundKey, int scoreKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            blackboard.SetBool(foundKey, _count > 0);
            blackboard.SetFloat(scoreKey, _count > 0 ? _results[0].Score : 0f);
        }

        /// <summary>Writes the best candidate position as an engine-free float triple.</summary>
        public void PublishBestPosition(RuntimeBlackboard blackboard, int positionKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            BTFloat3 position = _count > 0
                ? new BTFloat3(_results[0].X, _results[0].Y, _results[0].Z)
                : BTFloat3.Zero;
            blackboard.SetFloat3(positionKey, position);
        }

        /// <summary>Writes the caller-defined payload of the best candidate, or zero when nothing was found.</summary>
        public void PublishBestPayload(RuntimeBlackboard blackboard, int payloadKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            blackboard.SetInt(payloadKey, _count > 0 ? _results[0].Payload : 0);
        }

        /// <summary>Writes how many ranked candidates the last run kept.</summary>
        public void PublishCount(RuntimeBlackboard blackboard, int countKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            blackboard.SetInt(countKey, _count);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _count = 0;
            _results = NoPoints;
            _runner.Dispose();
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "EnvQueryBlackboardBridge must be used from the thread that created it; the runner keeps buffers between runs.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EnvQueryBlackboardBridge));
            }
        }
    }
}
