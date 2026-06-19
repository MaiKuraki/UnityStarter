using System;

namespace CycloneGames.Networking.Replication
{
    public readonly struct NetworkReplicationLoadSimulationOptions
    {
        public readonly int ConnectionCount;
        public readonly int ObjectCount;
        public readonly int TickCount;
        public readonly float WorldSize;
        public readonly float ViewRadius;
        public readonly float DirtyRatio;
        public readonly int BudgetBytes;
        public readonly int BudgetMessages;
        public readonly int ResultCapacity;
        public readonly uint Seed;

        public NetworkReplicationLoadSimulationOptions(
            int connectionCount,
            int objectCount,
            int tickCount,
            float worldSize,
            float viewRadius,
            float dirtyRatio,
            int budgetBytes,
            int budgetMessages,
            int resultCapacity,
            uint seed = 1u)
        {
            if (connectionCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionCount));
            }

            if (objectCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(objectCount));
            }

            if (tickCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickCount));
            }

            if (worldSize <= 0f || float.IsNaN(worldSize))
            {
                throw new ArgumentOutOfRangeException(nameof(worldSize));
            }

            if (viewRadius < 0f || float.IsNaN(viewRadius))
            {
                throw new ArgumentOutOfRangeException(nameof(viewRadius));
            }

            if (dirtyRatio < 0f || dirtyRatio > 1f || float.IsNaN(dirtyRatio))
            {
                throw new ArgumentOutOfRangeException(nameof(dirtyRatio));
            }

            if (budgetBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetBytes));
            }

            if (budgetMessages < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetMessages));
            }

            if (resultCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resultCapacity));
            }

            ConnectionCount = connectionCount;
            ObjectCount = objectCount;
            TickCount = tickCount;
            WorldSize = worldSize;
            ViewRadius = viewRadius;
            DirtyRatio = dirtyRatio;
            BudgetBytes = budgetBytes;
            BudgetMessages = budgetMessages;
            ResultCapacity = resultCapacity;
            Seed = seed == 0u ? 1u : seed;
        }
    }

    public readonly struct NetworkReplicationLoadSimulationResult
    {
        public readonly int TotalPlans;
        public readonly int TotalSelections;
        public readonly long TotalEstimatedBytes;
        public readonly float AverageSelectionsPerPlan;
        public readonly float AverageEstimatedBytesPerPlan;

        public NetworkReplicationLoadSimulationResult(
            int totalPlans,
            int totalSelections,
            long totalEstimatedBytes)
        {
            TotalPlans = totalPlans;
            TotalSelections = totalSelections;
            TotalEstimatedBytes = totalEstimatedBytes;
            AverageSelectionsPerPlan = totalPlans > 0 ? (float)totalSelections / totalPlans : 0f;
            AverageEstimatedBytesPerPlan = totalPlans > 0 ? (float)totalEstimatedBytes / totalPlans : 0f;
        }
    }

    public sealed class NetworkReplicationLoadSimulator
    {
        private readonly NetworkReplicationPlanner _planner;

        public NetworkReplicationLoadSimulator()
            : this(new NetworkReplicationPlanner())
        {
        }

        public NetworkReplicationLoadSimulator(NetworkReplicationPlanner planner)
        {
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        }

        public NetworkReplicationLoadSimulationResult Run(in NetworkReplicationLoadSimulationOptions options)
        {
            var random = new DeterministicRandom(options.Seed);
            NetworkReplicatedObject[] objects = new NetworkReplicatedObject[options.ObjectCount];
            NetworkReplicationSelection[] selections = new NetworkReplicationSelection[options.ResultCapacity];
            long totalBytes = 0L;
            int totalSelections = 0;
            int totalPlans = 0;

            for (int i = 0; i < objects.Length; i++)
            {
                NetworkVector3 position = NextPosition(ref random, options.WorldSize);
                bool isDirty = random.NextFloat() <= options.DirtyRatio;
                objects[i] = new NetworkReplicatedObject(
                    (ulong)i + 1UL,
                    NetworkReplicationPolicy.Area(options.ViewRadius, priority: 1f + random.NextFloat()),
                    position,
                    isDirty: isDirty,
                    estimatedPayloadBytes: 32 + random.NextInt(96));
            }

            for (int tick = 0; tick < options.TickCount; tick++)
            {
                for (int connection = 1; connection <= options.ConnectionCount; connection++)
                {
                    NetworkReplicationObserver observer = new NetworkReplicationObserver(
                        connection,
                        (ulong)connection,
                        teamId: connection % 4,
                        NextPosition(ref random, options.WorldSize),
                        options.ViewRadius);
                    var budget = new NetworkSendBudget(options.BudgetBytes, options.BudgetMessages);
                    int count = _planner.BuildPlan(observer, objects, tick, ref budget, selections);

                    totalPlans++;
                    totalSelections += count;
                    for (int i = 0; i < count; i++)
                    {
                        totalBytes += selections[i].EstimatedPayloadBytes;
                    }
                }
            }

            return new NetworkReplicationLoadSimulationResult(totalPlans, totalSelections, totalBytes);
        }

        private static NetworkVector3 NextPosition(ref DeterministicRandom random, float worldSize)
        {
            return new NetworkVector3(
                (random.NextFloat() - 0.5f) * worldSize,
                0f,
                (random.NextFloat() - 0.5f) * worldSize);
        }

        private struct DeterministicRandom
        {
            private uint _state;

            public DeterministicRandom(uint seed)
            {
                _state = seed;
            }

            public int NextInt(int maxExclusive)
            {
                return (int)(NextUInt() % (uint)maxExclusive);
            }

            public float NextFloat()
            {
                return (NextUInt() & 0x00FFFFFFu) / 16777216f;
            }

            private uint NextUInt()
            {
                _state = unchecked(_state * 1664525u + 1013904223u);
                return _state;
            }
        }
    }
}
