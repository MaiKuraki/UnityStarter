namespace CycloneGames.BehaviorTree.Core
{
    /// <summary>
    /// Deterministic random generator for replay, networking, and seeded nodes;
    /// the algorithm is fixed and platform-independent.
    /// </summary>
    public struct RuntimeDeterministicRandom
    {
        private uint _state;

        public RuntimeDeterministicRandom(uint seed)
        {
            _state = seed != 0u ? seed : 1u;
        }

        public uint State => _state;

        public static RuntimeDeterministicRandom FromState(uint state)
        {
            return new RuntimeDeterministicRandom(state);
        }

        public uint Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                return 0;
            }

            return NextInt(0, maxExclusive);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            uint range = (uint)(maxExclusive - minInclusive);
            uint threshold = (uint)(-range) % range;
            uint value;
            do
            {
                value = Next();
            }
            while (value < threshold);

            return minInclusive + (int)(value % range);
        }

        public float NextFloat()
        {
            return (Next() & 0x7FFFFFu) / (float)0x800000u;
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            return minInclusive + (maxInclusive - minInclusive) * NextFloat();
        }
    }

    internal static class RuntimeRandomUtility
    {
        public static float Range(RuntimeBlackboard blackboard, float minInclusive, float maxInclusive)
        {
            IRuntimeBTRandomProvider randomProvider = blackboard != null
                ? blackboard.GetService<IRuntimeBTRandomProvider>()
                : null;

            if (randomProvider != null)
            {
                return randomProvider.Range(minInclusive, maxInclusive);
            }

            return blackboard != null
                ? minInclusive + (maxInclusive - minInclusive) * blackboard.NextDefaultFloat()
                : minInclusive;
        }

        public static int RangeInt(RuntimeBlackboard blackboard, int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            IRuntimeBTRandomProvider randomProvider = blackboard != null
                ? blackboard.GetService<IRuntimeBTRandomProvider>()
                : null;

            if (randomProvider != null)
            {
                int value = (int)randomProvider.Range(minInclusive, maxExclusive);
                return value < minInclusive ? minInclusive :
                       value >= maxExclusive ? maxExclusive - 1 : value;
            }

            return blackboard != null
                ? blackboard.NextDefaultInt(minInclusive, maxExclusive)
                : minInclusive;
        }
    }
}
