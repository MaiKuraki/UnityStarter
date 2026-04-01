#if PRESENT_BURST && PRESENT_COLLECTIONS && PRESENT_MATHEMATICS
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.Factory.DOD.Runtime;

namespace CycloneGames.Factory.DODBullet
{
    /// <summary>
    /// Demonstrates NativePool for DOD bullet management.
    /// Compared to DODBulletManager_Jobs which manually tracks activeBulletCount + raw NativeArray,
    /// NativePool encapsulates the compact-array and swap-and-pop patterns with a clean API.
    /// </summary>
    public class DODBulletManager_NativePool : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] private Mesh bulletMesh;
        [SerializeField] private Material bulletMaterial;

        [Header("Spawning")]
        [SerializeField] private int maxBullets = 10000;
        [SerializeField] private float spawnsPerSecond = 1000f;
        [SerializeField] private Vector3 spawnAreaCenter = Vector3.zero;
        [SerializeField] private Vector3 spawnAreaSize = new Vector3(20, 20, 0);

        [Header("Bullet Settings")]
        [SerializeField] private float bulletSpeed = 10f;
        [SerializeField] private float bulletLifetime = 5f;
        [SerializeField] private Vector3 defaultVelocity = new Vector3(0, 0, 10f);
        [SerializeField] private float bulletScale = 1f;

        [Header("Homing")]
        [SerializeField] private bool enableHoming = true;
        [SerializeField] private Transform target;
        [SerializeField] private float homingStrength = 2.0f;

        [Header("Colors")]
        [SerializeField] private Color defaultColor = Color.white;

        private NativePool<Bullet_Jobs> _bulletPool;
        private NativeArray<Matrix4x4> _matrices;
        private NativeArray<Vector4> _colorArray;
        private NativeArray<bool> _despawnMask;
        private Matrix4x4[] _matricesForRender;
        private Vector4[] _colorsForRender;
        private MaterialPropertyBlock _propertyBlock;

        private float _spawnTimer;
        private float _spawnRate;
        private JobHandle _jobHandle;

        void Start()
        {
            _bulletPool = new NativePool<Bullet_Jobs>(maxBullets, Allocator.Persistent);
            _matrices = new NativeArray<Matrix4x4>(maxBullets, Allocator.Persistent);
            _colorArray = new NativeArray<Vector4>(maxBullets, Allocator.Persistent);
            _despawnMask = new NativeArray<bool>(maxBullets, Allocator.Persistent);
            _matricesForRender = new Matrix4x4[maxBullets];
            _colorsForRender = new Vector4[maxBullets];
            _propertyBlock = new MaterialPropertyBlock();
            _spawnRate = 1.0f / spawnsPerSecond;
        }

        void OnDestroy()
        {
            _jobHandle.Complete();
            _bulletPool.Dispose();
            if (_matrices.IsCreated) _matrices.Dispose();
            if (_colorArray.IsCreated) _colorArray.Dispose();
            if (_despawnMask.IsCreated) _despawnMask.Dispose();
        }

        void Update()
        {
            _jobHandle.Complete();
            HandleDespawning();
            HandleSpawning();
            ScheduleJobs();
        }

        void LateUpdate()
        {
            _jobHandle.Complete();
            RenderBullets();
        }

        void HandleSpawning()
        {
            _spawnTimer += Time.deltaTime;
            while (_spawnTimer >= _spawnRate && _bulletPool.ActiveCount < _bulletPool.Capacity)
            {
                _spawnTimer -= _spawnRate;

                float rx = UnityEngine.Random.Range(-spawnAreaSize.x * 0.5f, spawnAreaSize.x * 0.5f);
                float ry = UnityEngine.Random.Range(-spawnAreaSize.y * 0.5f, spawnAreaSize.y * 0.5f);
                float rz = UnityEngine.Random.Range(-spawnAreaSize.z * 0.5f, spawnAreaSize.z * 0.5f);

                _bulletPool.Spawn(new Bullet_Jobs
                {
                    Position = (float3)spawnAreaCenter + new float3(rx, ry, rz),
                    Velocity = math.normalize((float3)defaultVelocity) * bulletSpeed,
                    RemainingLifetime = bulletLifetime,
                    CurrentColor = new float4(defaultColor.r, defaultColor.g, defaultColor.b, defaultColor.a),
                    ColorResetTime = 0,
                    OldPosition = float3.zero
                });
            }
        }

        // Batch despawn using mask — avoids per-element swap overhead
        void HandleDespawning()
        {
            int count = _bulletPool.ActiveCount;
            if (count == 0) return;

            bool anyDespawn = false;
            var raw = _bulletPool.RawArray;
            for (int i = 0; i < count; i++)
            {
                bool dead = raw[i].RemainingLifetime <= 0;
                _despawnMask[i] = dead;
                anyDespawn |= dead;
            }

            if (anyDespawn)
            {
                _bulletPool.DespawnBatch(_despawnMask);
            }
        }

        void ScheduleJobs()
        {
            int count = _bulletPool.ActiveCount;
            if (count == 0) return;

            var updateJob = new UpdateBulletsJob_Jobs
            {
                Bullets = _bulletPool.RawArray.GetSubArray(0, count),
                DeltaTime = Time.deltaTime,
                EnableHoming = enableHoming && target != null,
                TargetPosition = target != null ? (float3)target.position : float3.zero,
                HomingStrength = homingStrength,
                BulletSpeed = bulletSpeed,
                DefaultColor = new float4(defaultColor.r, defaultColor.g, defaultColor.b, defaultColor.a)
            };
            _jobHandle = updateJob.Schedule(count, 64);

            var renderJob = new PrepareRenderJob_Jobs
            {
                Bullets = _bulletPool.RawArray.GetSubArray(0, count),
                Matrices = _matrices.GetSubArray(0, count),
                ColorArray = _colorArray.GetSubArray(0, count),
                Scale = new float3(bulletScale, bulletScale, bulletScale)
            };
            _jobHandle = renderJob.Schedule(count, 64, _jobHandle);

            JobHandle.ScheduleBatchedJobs();
        }

        void RenderBullets()
        {
            int count = _bulletPool.ActiveCount;
            if (count == 0 || bulletMesh == null || bulletMaterial == null) return;

            NativeArray<Matrix4x4>.Copy(_matrices, _matricesForRender, count);
            NativeArray<Vector4>.Copy(_colorArray, _colorsForRender, count);

            _propertyBlock.SetVectorArray("_BaseColor", _colorsForRender);
            Graphics.DrawMeshInstanced(bulletMesh, 0, bulletMaterial, _matricesForRender, count,
                _propertyBlock, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }
}
#endif
