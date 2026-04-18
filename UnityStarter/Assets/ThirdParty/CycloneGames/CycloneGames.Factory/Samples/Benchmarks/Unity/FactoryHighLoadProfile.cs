using UnityEngine;

namespace CycloneGames.Factory.Samples.Benchmarks.Unity
{
    /// <summary>
    /// Shared high-load benchmark profile so OOP, DOD and ECS suites can target the same stress envelope.
    /// </summary>
    [CreateAssetMenu(fileName = "FactoryHighLoadProfile", menuName = "CycloneGames/Factory/High Load Profile")]
    public sealed class FactoryHighLoadProfile : ScriptableObject
    {
        [Min(128)]
        public int softCapacity = 2048;

        [Min(256)]
        public int hardCapacity = 16384;

        [Min(256)]
        public int sustainedActiveCount = 10000;

        [Min(1)]
        public int spawnBurstPerFrame = 256;

        [Min(1)]
        public int updateBatchSize = 64;

        [Min(1f)]
        public float benchmarkDurationSeconds = 10f;
    }
}
