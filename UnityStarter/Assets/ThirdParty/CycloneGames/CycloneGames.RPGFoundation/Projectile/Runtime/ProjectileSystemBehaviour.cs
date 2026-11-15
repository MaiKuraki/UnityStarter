using System;
using CycloneGames.RPGFoundation.Projectile.Core;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Projectile.Runtime
{
    /// <summary>
    /// Scene-owned composition root. Drives the world with a fixed simulation step so
    /// tick-indexed state is identical across frame rates and hardware tiers; a bounded
    /// catch-up budget prevents a slow frame from turning into a death spiral.
    /// </summary>
    public class ProjectileSystemBehaviour : MonoBehaviour
    {
        [SerializeField] private ProjectileSimulationPlane SimulationPlane = ProjectileSimulationPlane.Full3D;
        [SerializeField] private ProjectileCollisionMode CollisionMode = ProjectileCollisionMode.Physics3D;
        [SerializeField] private int ProjectileCapacity = 1024;
        [SerializeField] private int EventCapacity = 256;
        [SerializeField] private int CollisionHitCapacity = 8;
        [SerializeField] private int MaxCollisionIterations = 4;
        [SerializeField] private float LockedAxisValue;
        [SerializeField] private Vector3 Gravity = new Vector3(0f, -9.81f, 0f);
        [SerializeField] private float FixedDeltaTime = 1f / 60f;
        [SerializeField] private int MaxSubSteps = 4;
        [SerializeField] private float MaxFrameDeltaTime = 0.25f;
        [SerializeField] private bool AutoTick = true;

        private ProjectileWorld _world;
        private IProjectileCollisionWorld _collisionWorld;
        private int _tick;
        private float _accumulator;

        public event Action<ProjectileHitEvent> Hit;

        public ProjectileWorld World
        {
            get
            {
                EnsureInitialized();
                return _world;
            }
        }

        public bool IsInitialized => _world != null;

        public int CurrentTick => _tick;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            if (!AutoTick)
            {
                return;
            }

            float fixedDelta = Clamp(FixedDeltaTime, 1f / 240f, 0.25f);
            float frameBudget = Clamp(MaxFrameDeltaTime, fixedDelta, 1f);
            int subSteps = Clamp(MaxSubSteps, 1, 8);
            float frameDelta = Time.deltaTime;
            if (float.IsNaN(frameDelta) || frameDelta < 0f)
            {
                frameDelta = 0f;
            }

            if (frameDelta > frameBudget)
            {
                frameDelta = frameBudget;
            }

            _accumulator += frameDelta;
            int steps = 0;
            while (_accumulator >= fixedDelta && steps < subSteps)
            {
                Step(fixedDelta, _tick + 1, null);
                _accumulator -= fixedDelta;
                steps++;
            }

            if (steps == subSteps)
            {
                _accumulator = 0f;
            }
        }

        public bool TrySpawn(
            ProjectileDefinitionAsset definitionAsset,
            Vector3 position,
            Vector3 direction,
            ulong ownerEntityId,
            ulong networkEntityId,
            out ProjectileHandle handle,
            ulong targetEntityId = 0UL,
            int predictionKey = 0,
            uint seed = 0u)
        {
            if (definitionAsset == null)
            {
                handle = default;
                return false;
            }

            EnsureInitialized();

            ProjectileDefinition definition = definitionAsset.BuildDefinition();
            var request = new ProjectileSpawnRequest(
                definition,
                ownerEntityId,
                networkEntityId,
                targetEntityId,
                _tick,
                predictionKey,
                seed,
                ToProjectileVector3(position),
                ToProjectileVector3(direction),
                ProjectileVector3.Zero);

            return _world.TrySpawn(in request, out handle);
        }

        public void Step(float deltaTime, int tick, IProjectileTargetProvider targetProvider)
        {
            EnsureInitialized();
            _tick = tick;
            _world.Step(deltaTime, tick, _collisionWorld, targetProvider);
            DispatchHits();
        }

        public bool TryGetSnapshot(ProjectileHandle handle, out ProjectileSnapshot snapshot)
        {
            EnsureInitialized();
            return _world.TryGetSnapshot(handle, out snapshot);
        }

        /// <summary>
        /// Sequential authoritative batch. The caller owns the destination array, so the
        /// whole broadcast is one dense pass with no per-projectile lookup and no allocation.
        /// </summary>
        public int CaptureSnapshots(ProjectileSnapshot[] destination, int startIndex = 0)
        {
            EnsureInitialized();
            return _world.CaptureSnapshots(destination, startIndex);
        }

        public void Clear()
        {
            EnsureInitialized();
            _world.Clear();
            _tick = 0;
            _accumulator = 0f;
        }

        private void EnsureInitialized()
        {
            if (_world != null)
            {
                return;
            }

            ProjectileSpaceProfile space = CreateSpaceProfile();
            _world = CreateWorld(in space);
            _collisionWorld = CreateCollisionWorld();
        }

        protected virtual ProjectileSpaceProfile CreateSpaceProfile()
        {
            return new ProjectileSpaceProfile(
                SimulationPlane,
                LockedAxisValue,
                ToProjectileVector3(Gravity));
        }

        protected virtual ProjectileWorld CreateWorld(in ProjectileSpaceProfile space)
        {
            float fixedDelta = Clamp(FixedDeltaTime, 1f / 240f, 0.25f);
            var config = new ProjectileWorldConfig(
                Math.Max(1, ProjectileCapacity),
                Math.Max(1, EventCapacity),
                Math.Max(1, CollisionHitCapacity),
                in space,
                Clamp(MaxCollisionIterations, 1, 16),
                fixedDelta,
                Clamp(MaxSubSteps, 1, 8));
            return new ProjectileWorld(in config);
        }

        protected virtual IProjectileCollisionWorld CreateCollisionWorld()
        {
            switch (CollisionMode)
            {
                case ProjectileCollisionMode.Physics3D:
                    return new UnityProjectileCollisionWorld3D(Math.Max(1, CollisionHitCapacity));
                case ProjectileCollisionMode.Physics2D:
                    return new UnityProjectileCollisionWorld2D(Math.Max(1, CollisionHitCapacity));
                default:
                    return null;
            }
        }

        protected virtual void DispatchHits()
        {
            ProjectileEventBuffer hits = _world.HitEvents;
            Action<ProjectileHitEvent> callback = Hit;

            for (int i = 0; i < hits.Count; i++)
            {
                ProjectileHitEvent hitEvent = hits[i];
                OnHitDispatching(in hitEvent);
                callback?.Invoke(hitEvent);
            }
        }

        protected virtual void OnHitDispatching(in ProjectileHitEvent hitEvent)
        {
        }

        private static ProjectileVector3 ToProjectileVector3(Vector3 value)
        {
            return new ProjectileVector3(value.x, value.y, value.z);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value) || value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
