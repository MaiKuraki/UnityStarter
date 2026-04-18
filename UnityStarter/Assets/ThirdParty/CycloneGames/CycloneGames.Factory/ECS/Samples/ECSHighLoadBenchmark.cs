#if PRESENT_BURST && PRESENT_ECS
using Unity.Entities;
using UnityEngine;

namespace CycloneGames.Factory.ECS.Samples
{
    public struct ECSHighLoadBenchmarkConfig : IComponentData
    {
        public int TargetActiveCount;
        public float ReportInterval;
    }

    public sealed class ECSHighLoadBenchmarkAuthoring : MonoBehaviour
    {
        public int TargetActiveCount = 10000;
        public float ReportInterval = 1f;

        private sealed class Baker : Baker<ECSHighLoadBenchmarkAuthoring>
        {
            public override void Bake(ECSHighLoadBenchmarkAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new ECSHighLoadBenchmarkConfig
                {
                    TargetActiveCount = authoring.TargetActiveCount,
                    ReportInterval = Mathf.Max(0.25f, authoring.ReportInterval)
                });
            }
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ECSHighLoadBenchmarkSystem : SystemBase
    {
        private double _nextReportTime;

        protected override void OnCreate()
        {
            RequireForUpdate<ECSHighLoadBenchmarkConfig>();
            RequireForUpdate<BulletComponent>();
            RequireForUpdate<BulletPoolMetrics>();
        }

        protected override void OnUpdate()
        {
            var config = SystemAPI.GetSingleton<ECSHighLoadBenchmarkConfig>();
            var metrics = SystemAPI.GetSingleton<BulletPoolMetrics>();
            double elapsed = SystemAPI.Time.ElapsedTime;
            if (elapsed < _nextReportTime)
            {
                return;
            }

            Debug.Log(
                $"[Factory][ECS][{(BulletSpawnStrategy)metrics.SpawnStrategy}] CountActive={metrics.CountActive}/{config.TargetActiveCount}, CountInactive={metrics.CountInactive}, CountAll={metrics.CountAll}, " +
                $"PeakCountActive={metrics.PeakCountActive}, TotalCreated={metrics.TotalCreated}, TotalSpawned={metrics.TotalSpawned}, TotalDespawned={metrics.TotalDespawned}, " +
                $"RejectedSpawns={metrics.RejectedSpawns}, InvalidDespawns={metrics.InvalidDespawns}");
            _nextReportTime = elapsed + config.ReportInterval;
        }
    }
}
#endif
