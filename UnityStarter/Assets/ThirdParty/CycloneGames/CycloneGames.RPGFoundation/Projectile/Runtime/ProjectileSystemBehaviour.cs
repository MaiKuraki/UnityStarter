using System;
using CycloneGames.RPGFoundation.Projectile.Core;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Projectile.Runtime
{
    public class ProjectileSystemBehaviour : MonoBehaviour
    {
        [SerializeField] private ProjectileSimulationPlane SimulationPlane = ProjectileSimulationPlane.Full3D;
        [SerializeField] private ProjectileCollisionMode CollisionMode = ProjectileCollisionMode.Physics3D;
        [SerializeField] private int ProjectileCapacity = 1024;
        [SerializeField] private int EventCapacity = 256;
        [SerializeField] private int CollisionHitCapacity = 8;
        [SerializeField] private float LockedAxisValue;
        [SerializeField] private Vector3 Gravity = new Vector3(0f, -9.81f, 0f);
        [SerializeField] private bool AutoTick = true;

        private ProjectileWorld _world;
        private IProjectileCollisionWorld _collisionWorld;
        private int _tick;

        public event Action<ProjectileHitEvent> Hit;

        public ProjectileWorld World
        {
            get
            {
                EnsureInitialized();
                return _world;
            }
        }

        public bool IsInitialized
        {
            get
            {
                return _world != null;
            }
        }

        public int CurrentTick
        {
            get
            {
                return _tick;
            }
        }

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

            Step(Time.deltaTime, _tick + 1, null);
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

        public void Clear()
        {
            EnsureInitialized();
            _world.Clear();
            _tick = 0;
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
            return new ProjectileWorld(
                Math.Max(1, ProjectileCapacity),
                Math.Max(1, EventCapacity),
                Math.Max(1, CollisionHitCapacity),
                in space);
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
    }
}
