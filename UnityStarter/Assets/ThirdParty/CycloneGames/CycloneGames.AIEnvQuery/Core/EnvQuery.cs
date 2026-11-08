using System;

namespace CycloneGames.AIEnvQuery.Core
{
    /// <summary>World position of one candidate, plus the score accumulated by the tests.</summary>
    public struct EnvQueryPoint
    {
        public float X;
        public float Y;
        public float Z;

        /// <summary>Accumulated score. Higher is better; tests only add to it.</summary>
        public float Score;

        /// <summary>
        /// Caller-defined identity, carried through scoring and ordering untouched. A generator that
        /// produces points from data writes the row index or identifier here so the caller can map the
        /// winning point back without a position lookup.
        /// </summary>
        public int Payload;

        public EnvQueryPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
            Score = 0f;
            Payload = 0;
        }
    }

    /// <summary>
    /// Everything a query needs about the asker and its current target. Passed by <c>in</c> so a run
    /// never copies it per candidate.
    /// </summary>
    public struct EnvQueryContext
    {
        public float QuerierX;
        public float QuerierY;
        public float QuerierZ;

        /// <summary>Normalised facing of the querier. Zero when the query ignores facing.</summary>
        public float ForwardX;
        public float ForwardY;
        public float ForwardZ;

        public float TargetX;
        public float TargetY;
        public float TargetZ;

        /// <summary>False when the target position is unknown; target-dependent tests then score zero.</summary>
        public bool HasTarget;

        /// <summary>
        /// Project data for the tests of one query, for example a cover table, a navigation handle or
        /// a server-side collision world. Never written by the runner and never inspected by it.
        /// </summary>
        public object UserData;

        public void SetForward(float x, float y, float z)
        {
            float lengthSquared = x * x + y * y + z * z;
            if (lengthSquared <= 1e-12f)
            {
                ForwardX = 0f;
                ForwardY = 0f;
                ForwardZ = 0f;
                return;
            }

            float inverse = 1f / MathF.Sqrt(lengthSquared);
            ForwardX = x * inverse;
            ForwardY = y * inverse;
            ForwardZ = z * inverse;
        }
    }

    /// <summary>Produces candidate positions. Implementations must not allocate.</summary>
    public interface IEnvQueryGenerator
    {
        /// <summary>
        /// Writes candidates into <paramref name="points"/> and returns how many were written.
        /// The buffer is reused between runs, so a generator must not assume a zeroed or empty buffer.
        /// </summary>
        int Generate(in EnvQueryContext context, EnvQueryPoint[] points);
    }

    /// <summary>Adjusts one candidate's score. Implementations must not allocate.</summary>
    public interface IEnvQueryTest
    {
        /// <summary>Weight applied to this test when the runner combines scores.</summary>
        float Weight { get; }

        /// <summary>
        /// Adds to <see cref="EnvQueryPoint.Score"/>. Implementations should write a value in 0..1 so
        /// weights stay comparable across queries.
        /// </summary>
        void Score(in EnvQueryContext context, ref EnvQueryPoint point);
    }

    /// <summary>
    /// Rejects a candidate before any test runs.
    /// </summary>
    /// <remarks>
    /// Filters remove a point from the run; tests only adjust its score. Use a filter for conditions
    /// that make a position unusable, such as an unreachable point or a disabled slot, and a test for
    /// qualities that make it better or worse.
    /// </remarks>
    public interface IEnvQueryFilter
    {
        bool Accept(in EnvQueryContext context, in EnvQueryPoint point);
    }

    /// <summary>
    /// Generates candidates, scores them and returns the best ones.
    /// </summary>
    /// <remarks>
    /// Buffers are allocated at construction. <see cref="Run"/> allocates nothing, so the same runner
    /// can be used every frame from one owner thread. Ties are broken by the candidate ordinal, which
    /// keeps the result deterministic for a fixed generator and context.
    /// </remarks>
    public sealed class EnvQueryRunner : IDisposable
    {
        private readonly EnvQueryPoint[] _candidates;
        private readonly float[] _scores;
        private readonly int[] _order;
        private bool _disposed;

        /// <param name="maxCandidates">Upper bound on generated candidates per run.</param>
        public EnvQueryRunner(int maxCandidates = 256)
        {
            if (maxCandidates < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCandidates), maxCandidates, "At least one candidate must be allowed.");
            }

            _candidates = new EnvQueryPoint[maxCandidates];
            _scores = new float[maxCandidates];
            _order = new int[maxCandidates];
        }

        public int MaxCandidates => _candidates.Length;

        /// <summary>
        /// Runs the query and writes at most <paramref name="results"/> entries, best score first.
        /// </summary>
        /// <param name="generator">Candidate source.</param>
        /// <param name="tests">Scoring passes; each contributes <c>score * weight</c>.</param>
        /// <param name="context">Querier and target description.</param>
        /// <param name="results">Destination for the ranked candidates.</param>
        /// <returns>Number of candidates written.</returns>
        public int Run(
            IEnvQueryGenerator generator,
            IEnvQueryTest[] tests,
            in EnvQueryContext context,
            EnvQueryPoint[] results)
        {
            return Run(generator, null, tests, in context, results);
        }

        /// <summary>
        /// Runs the query with a filter pass between generation and scoring.
        /// </summary>
        /// <param name="filters">Rejects candidates before any test runs; may be null.</param>
        public int Run(
            IEnvQueryGenerator generator,
            IEnvQueryFilter[] filters,
            IEnvQueryTest[] tests,
            in EnvQueryContext context,
            EnvQueryPoint[] results)
        {
            if (generator == null)
            {
                throw new ArgumentNullException(nameof(generator));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            ThrowIfDisposed();

            int count = generator.Generate(in context, _candidates);
            if (count > _candidates.Length)
            {
                count = _candidates.Length;
            }

            count = ApplyFilters(filters, in context, count);

            int testCount = tests?.Length ?? 0;
            for (int i = 0; i < testCount; i++)
            {
                IEnvQueryTest test = tests[i];
                if (test == null)
                {
                    continue;
                }

                float weight = test.Weight;
                for (int candidate = 0; candidate < count; candidate++)
                {
                    EnvQueryPoint point = _candidates[candidate];
                    point.Score = 0f;
                    test.Score(in context, ref point);
                    _candidates[candidate].Score += point.Score * weight;
                }
            }

            int resultCount = Math.Min(count, results.Length);
            for (int i = 0; i < count; i++)
            {
                _scores[i] = _candidates[i].Score;
                _order[i] = i;
            }

            SortByScore(count);

            for (int i = 0; i < resultCount; i++)
            {
                results[i] = _candidates[_order[i]];
            }

            return resultCount;
        }

        /// <summary>Compacts the candidate buffer in place, keeping the generator order.</summary>
        private int ApplyFilters(IEnvQueryFilter[] filters, in EnvQueryContext context, int count)
        {
            if (filters == null || filters.Length == 0)
            {
                return count;
            }

            int write = 0;
            for (int read = 0; read < count; read++)
            {
                EnvQueryPoint candidate = _candidates[read];
                bool accepted = true;
                for (int i = 0; i < filters.Length; i++)
                {
                    IEnvQueryFilter filter = filters[i];
                    if (filter != null && !filter.Accept(in context, in candidate))
                    {
                        accepted = false;
                        break;
                    }
                }

                if (accepted)
                {
                    _candidates[write++] = candidate;
                }
            }

            return write;
        }

        public void Dispose()
        {
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EnvQueryRunner));
            }
        }

        /// <summary>
        /// In-place heapsort on the index array: best score first, ordinal ascending on ties.
        /// </summary>
        /// <remarks>
        /// The BCL <c>Array.Sort</c> overload that takes an <c>IComparer</c> allocates per call, and
        /// this path runs once per query, so the runner carries its own sort instead.
        /// </remarks>
        private void SortByScore(int count)
        {
            for (int i = (count >> 1) - 1; i >= 0; i--)
            {
                SiftDown(i, count);
            }

            for (int end = count - 1; end > 0; end--)
            {
                int worst = _order[0];
                _order[0] = _order[end];
                _order[end] = worst;
                SiftDown(0, end);
            }
        }

        private void SiftDown(int root, int count)
        {
            int current = root;
            while (true)
            {
                int left = (current << 1) + 1;
                if (left >= count)
                {
                    return;
                }

                int worst = current;
                if (IsWorse(_order[left], _order[worst]))
                {
                    worst = left;
                }

                int right = left + 1;
                if (right < count && IsWorse(_order[right], _order[worst]))
                {
                    worst = right;
                }

                if (worst == current)
                {
                    return;
                }

                int swap = _order[current];
                _order[current] = _order[worst];
                _order[worst] = swap;
                current = worst;
            }
        }

        private bool IsWorse(int left, int right)
        {
            float leftScore = _scores[left];
            float rightScore = _scores[right];
            if (leftScore != rightScore)
            {
                return leftScore < rightScore;
            }

            return left > right;
        }
    }
}
