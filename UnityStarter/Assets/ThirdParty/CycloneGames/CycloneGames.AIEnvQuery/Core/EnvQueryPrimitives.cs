using System;

namespace CycloneGames.AIEnvQuery.Core
{
    /// <summary>
    /// Line-of-sight probe supplied by the host. Return true when nothing blocks the segment.
    /// </summary>
    public delegate bool EnvQueryLineOfSight(
        float fromX, float fromY, float fromZ,
        float toX, float toY, float toZ);

    /// <summary>
    /// Candidates on a regular grid centred on the querier, clipped to a radius.
    /// </summary>
    /// <remarks>Extents are derived from the radius and spacing.</remarks>
    public sealed class GridAroundQuerierGenerator : IEnvQueryGenerator
    {
        private readonly float _spacing;
        private readonly float _radius;
        private readonly float _verticalOffset;

        public GridAroundQuerierGenerator(float spacing, float radius, float verticalOffset = 0f)
        {
            if (!(spacing > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(spacing), spacing, "Spacing must be positive.");
            }

            if (!(radius > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be positive.");
            }

            _spacing = spacing;
            _radius = radius;
            _verticalOffset = verticalOffset;
        }

        public int Generate(in EnvQueryContext context, EnvQueryPoint[] points)
        {
            int steps = Math.Max(0, (int)MathF.Floor(_radius / _spacing));
            int capacity = points.Length;
            int written = 0;
            float radiusSquared = _radius * _radius;

            for (int z = -steps; z <= steps && written < capacity; z++)
            {
                float dz = z * _spacing;
                for (int x = -steps; x <= steps && written < capacity; x++)
                {
                    float dx = x * _spacing;
                    if (dx * dx + dz * dz > radiusSquared)
                    {
                        continue;
                    }

                    points[written++] = new EnvQueryPoint(
                        context.QuerierX + dx,
                        context.QuerierY + _verticalOffset,
                        context.QuerierZ + dz);
                }
            }

            return written;
        }
    }

    /// <summary>
    /// Candidates taken from a fixed list, optionally filtered by distance from the querier.
    /// </summary>
    /// <remarks>The list is owned by the caller and never copied.</remarks>
    public sealed class PointListGenerator : IEnvQueryGenerator
    {
        private readonly EnvQueryPoint[] _source;
        private readonly float _maximumDistanceSquared;

        /// <param name="maximumDistance">Zero or negative disables the distance filter.</param>
        public PointListGenerator(EnvQueryPoint[] source, float maximumDistance = 0f)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _maximumDistanceSquared = maximumDistance > 0f ? maximumDistance * maximumDistance : 0f;
        }

        public int Generate(in EnvQueryContext context, EnvQueryPoint[] points)
        {
            int capacity = Math.Min(points.Length, _source.Length);
            int written = 0;

            for (int i = 0; i < capacity; i++)
            {
                EnvQueryPoint candidate = _source[i];
                if (_maximumDistanceSquared > 0f)
                {
                    float dx = candidate.X - context.QuerierX;
                    float dy = candidate.Y - context.QuerierY;
                    float dz = candidate.Z - context.QuerierZ;
                    if (dx * dx + dy * dy + dz * dz > _maximumDistanceSquared)
                    {
                        continue;
                    }
                }

                points[written++] = candidate;
            }

            return written;
        }
    }

    /// <summary>
    /// Scores a candidate by its distance to the querier or to the target: closer is better by
    /// default, farther when <c>preferFarther</c> is set.
    /// </summary>
    public sealed class DistanceTest : IEnvQueryTest
    {
        private readonly bool _useTarget;
        private readonly bool _preferFarther;
        private readonly float _range;

        /// <param name="range">Distance that maps to a zero score. Must be positive.</param>
        public DistanceTest(float range, float weight = 1f, bool useTarget = false, bool preferFarther = false)
        {
            if (!(range > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(range), range, "Range must be positive.");
            }

            _range = range;
            Weight = weight;
            _useTarget = useTarget;
            _preferFarther = preferFarther;
        }

        public float Weight { get; }

        public void Score(in EnvQueryContext context, ref EnvQueryPoint point)
        {
            float originX = _useTarget ? context.TargetX : context.QuerierX;
            float originY = _useTarget ? context.TargetY : context.QuerierY;
            float originZ = _useTarget ? context.TargetZ : context.QuerierZ;

            if (_useTarget && !context.HasTarget)
            {
                return;
            }

            float dx = point.X - originX;
            float dy = point.Y - originY;
            float dz = point.Z - originZ;
            float normalized = MathF.Sqrt(dx * dx + dy * dy + dz * dz) / _range;
            if (normalized > 1f)
            {
                normalized = 1f;
            }

            point.Score += _preferFarther ? normalized : 1f - normalized;
        }
    }

    /// <summary>Scores a candidate by how well it sits in front of the querier.</summary>
    public sealed class FacingTest : IEnvQueryTest
    {
        public FacingTest(float weight = 1f)
        {
            Weight = weight;
        }

        public float Weight { get; }

        public void Score(in EnvQueryContext context, ref EnvQueryPoint point)
        {
            float dx = point.X - context.QuerierX;
            float dy = point.Y - context.QuerierY;
            float dz = point.Z - context.QuerierZ;
            float lengthSquared = dx * dx + dy * dy + dz * dz;
            if (lengthSquared <= 1e-12f)
            {
                return;
            }

            float inverse = 1f / MathF.Sqrt(lengthSquared);
            float dot = (dx * context.ForwardX + dy * context.ForwardY + dz * context.ForwardZ) * inverse;
            point.Score += (dot + 1f) * 0.5f;
        }
    }

    /// <summary>Scores a candidate by whether the host reports a clear line of sight.</summary>
    public sealed class LineOfSightTest : IEnvQueryTest
    {
        private readonly EnvQueryLineOfSight _trace;
        private readonly float _eyeHeight;

        public LineOfSightTest(EnvQueryLineOfSight trace, float weight = 1f, float eyeHeight = 0f)
        {
            _trace = trace ?? throw new ArgumentNullException(nameof(trace));
            Weight = weight;
            _eyeHeight = eyeHeight;
        }

        public float Weight { get; }

        public void Score(in EnvQueryContext context, ref EnvQueryPoint point)
        {
            if (_trace(
                    context.QuerierX,
                    context.QuerierY + _eyeHeight,
                    context.QuerierZ,
                    point.X,
                    point.Y + _eyeHeight,
                    point.Z))
            {
                point.Score += 1f;
            }
        }
    }
}
