using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Stable deterministic random generator for replay, networking, and seeded behavior nodes.
    /// The algorithm is intentionally fixed and platform-independent.
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

            return randomProvider != null
                ? randomProvider.Range(minInclusive, maxInclusive)
                : Random.Range(minInclusive, maxInclusive);
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
                return Mathf.Clamp(value, minInclusive, maxExclusive - 1);
            }

            return Random.Range(minInclusive, maxExclusive);
        }
    }
}
