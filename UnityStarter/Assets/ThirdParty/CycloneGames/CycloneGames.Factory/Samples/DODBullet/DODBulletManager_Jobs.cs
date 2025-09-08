#if PRESENT_BURST && PRESENT_COLLECTIONS && PRESENT_MATHEMATICS
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.Factory.DODBullet
{
    public struct Bullet_Jobs
    {
        public float3 Position;
        public float3 Velocity;
        public float RemainingLifetime;
        public float4 CurrentColor;
        public float ColorResetTime;
        public float3 OldPosition; // Store old position for Collection Check
    }

    [BurstCompile]
    public struct UpdateBulletsJob_Jobs : IJobParallelFor
    {
        public NativeArray<Bullet_Jobs> Bullets;
        
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public bool EnableHoming;
        [ReadOnly] public float3 TargetPosition;
        [ReadOnly] public float HomingStrength;
        [ReadOnly] public float BulletSpeed;
        [ReadOnly] public float4 DefaultColor;

        public void Execute(int index)
        {
            var bullet = Bullets[index];

            bullet.RemainingLifetime -= DeltaTime;
            
            if (bullet.RemainingLifetime <= 0)
            {
                Bullets[index] = bullet;
                return;
            }

            if (bullet.ColorResetTime > 0)
            {
                bullet.ColorResetTime -= DeltaTime;
                if (bullet.ColorResetTime <= 0)
                {
                    bullet.CurrentColor = DefaultColor;
                }
            }
            
            if (EnableHoming)
            {
                float3 directionToTarget = math.normalize(TargetPosition - bullet.Position);
                bullet.Velocity = math.normalize(math.lerp(math.normalize(bullet.Velocity), directionToTarget, HomingStrength * DeltaTime)) * BulletSpeed;
            }
            
            bullet.OldPosition = bullet.Position;
            bullet.Position += bullet.Velocity * DeltaTime;
            
            Bullets[index] = bullet;
        }
    }

    [BurstCompile]
    public struct PrepareRenderJob_Jobs : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Bullet_Jobs> Bullets;
        public NativeArray<Matrix4x4> Matrices;
        public NativeArray<Vector4> ColorArray;
        [ReadOnly] public float3 Scale;

        public void Execute(int index)
        {
            var bullet = Bullets[index];
            Matrices[index] = Matrix4x4.TRS(bullet.Position, quaternion.identity, Scale);
            ColorArray[index] = new Vector4(bullet.CurrentColor.x, bullet.CurrentColor.y, bullet.CurrentColor.z, bullet.CurrentColor.w);
        }
    }
    
    public class DODBulletManager_Jobs : MonoBehaviour
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

        [Header("Interaction")]
        [Tooltip("A BoxCollider defining the area where collision checks are active. Bullets outside this zone will not be checked.")]
        [SerializeField] private BoxCollider interactionZone;
        [SerializeField] private LayerMask collisionLayers;
        [SerializeField] private float collisionRadius = 0.5f;
        
        [Header("Homing")]
        [SerializeField] private bool enableHoming = true;
        [SerializeField] private Transform target;
        [SerializeField] private float homingStrength = 2.0f;
        
        [Header("Colors")]
        [SerializeField] private Color defaultColor = Color.white;
        [SerializeField] private Color collisionColor = Color.red;
        [SerializeField] private float collisionColorDuration = 0.2f;

        private Bounds interactionBounds;
        private MaterialPropertyBlock propertyBlock;
        
        private NativeArray<Bullet_Jobs> bullets;
        private NativeArray<Matrix4x4> matrices;
        private NativeArray<Vector4> colorArray;

        // Managed arrays for rendering, as Graphics APIs don't directly accept NativeArrays.
        private Matrix4x4[] matricesForRender;
        private Vector4[] colorsForRender;
        
        private int activeBulletCount = 0;
        private float spawnTimer;
        private float spawnRate;
        private JobHandle updateJobHandle;

        void Start()
        {
            // Use persistent allocator for NativeArrays that live across frames
            bullets = new NativeArray<Bullet_Jobs>(maxBullets, Allocator.Persistent);
            matrices = new NativeArray<Matrix4x4>(maxBullets, Allocator.Persistent);
            colorArray = new NativeArray<Vector4>(maxBullets, Allocator.Persistent);

            // Initialize managed arrays for rendering
            matricesForRender = new Matrix4x4[maxBullets];
            colorsForRender = new Vector4[maxBullets];
            
            propertyBlock = new MaterialPropertyBlock();
            spawnRate = 1.0f / spawnsPerSecond;
            spawnTimer = 0;

            if (interactionZone != null)
            {
                interactionBounds = interactionZone.bounds;
                interactionZone.enabled = false; 
            }
            else if(collisionLayers.value != 0)
            {
                // UnityEngine.Debug.LogWarning("Collision Layers are set, but no Interaction Zone is defined. Collisions will be checked everywhere, which may be slow.");
            }
        }

        void OnDestroy()
        {
            updateJobHandle.Complete();
            if (bullets.IsCreated) bullets.Dispose();
            if (matrices.IsCreated) matrices.Dispose();
            if (colorArray.IsCreated) colorArray.Dispose();
        }

        void Update()
        {
            // Complete the job from the *previous* frame at the beginning of the *current* frame.
            // This gives the jobs maximum time to run in the background.
            updateJobHandle.Complete();

            HandleSpawning();
            HandleDespawning();

            var updateBulletsJob = new UpdateBulletsJob_Jobs
            {
                Bullets = bullets.GetSubArray(0, activeBulletCount),
                DeltaTime = Time.deltaTime,
                EnableHoming = enableHoming && target != null,
                TargetPosition = target != null ? (float3)target.position : float3.zero,
                HomingStrength = homingStrength,
                BulletSpeed = bulletSpeed,
                DefaultColor = new float4(defaultColor.r, defaultColor.g, defaultColor.b, defaultColor.a)
            };
            updateJobHandle = updateBulletsJob.Schedule(activeBulletCount, 32);
            
            var prepareRenderJob = new PrepareRenderJob_Jobs
            {
                Bullets = bullets.GetSubArray(0, activeBulletCount),
                Matrices = matrices.GetSubArray(0, activeBulletCount),
                ColorArray = colorArray.GetSubArray(0, activeBulletCount),
                Scale = new float3(bulletScale, bulletScale, bulletScale)
            };
            updateJobHandle = prepareRenderJob.Schedule(activeBulletCount, 32, updateJobHandle);
            
            JobHandle.ScheduleBatchedJobs();
        }

        void LateUpdate()
        {
            updateJobHandle.Complete();
            
            // Main-thread work (physics) is done after the jobs are complete.
            HandleCollisions();
            RenderBullets();
        }

        void HandleSpawning()
        {
            spawnTimer += Time.deltaTime;
            int spawnCount = 0;
            while (spawnTimer >= spawnRate)
            {
                spawnTimer -= spawnRate;
                if (activeBulletCount < maxBullets)
                {
                    spawnCount++;
                }
            }

            for (int i = 0; i < spawnCount; i++)
            {
                var bullet = bullets[activeBulletCount];
                
                // Use UnityEngine.Random explicitly to avoid ambiguity with Unity.Mathematics.Random
                float randomX = UnityEngine.Random.Range(-spawnAreaSize.x / 2, spawnAreaSize.x / 2);
                float randomY = UnityEngine.Random.Range(-spawnAreaSize.y / 2, spawnAreaSize.y / 2);
                float randomZ = UnityEngine.Random.Range(-spawnAreaSize.z / 2, spawnAreaSize.z / 2);

                bullet.Position = (float3)spawnAreaCenter + new float3(randomX, randomY, randomZ);
                bullet.Velocity = math.normalize((float3)defaultVelocity) * bulletSpeed;
                bullet.RemainingLifetime = bulletLifetime;
                bullet.CurrentColor = new float4(defaultColor.r, defaultColor.g, defaultColor.b, defaultColor.a);
                bullet.ColorResetTime = 0;
                
                bullets[activeBulletCount] = bullet;
                activeBulletCount++;
            }
        }

        void HandleDespawning()
        {
            for (int i = activeBulletCount - 1; i >= 0; i--)
            {
                if (bullets[i].RemainingLifetime <= 0)
                {
                    // Swap with the last active bullet and decrement count
                    bullets[i] = bullets[activeBulletCount - 1];
                    activeBulletCount--;
                }
            }
        }
        
        void HandleCollisions()
        {
            float scaledCollisionRadius = collisionRadius * bulletScale;
            var collisionFloat4 = new float4(collisionColor.r, collisionColor.g, collisionColor.b, collisionColor.a);

            for (int i = 0; i < activeBulletCount; i++)
            {
                if (collisionLayers.value != 0)
                {
                    bool shouldCheckCollision = (interactionZone == null) || interactionBounds.Contains((Vector3)bullets[i].Position);
                    if (shouldCheckCollision)
                    {
                        var bullet = bullets[i];
                        float3 movement = bullet.Position - bullet.OldPosition;
                        float movementDistance = math.length(movement);

                        if (movementDistance > 0 && Physics.SphereCast((Vector3)bullet.OldPosition, scaledCollisionRadius, math.normalize(movement), out RaycastHit hit, movementDistance, collisionLayers))
                        {
                            // UnityEngine.Debug.Log($"Bullet collided with {hit.collider.name}");
                            bullet.CurrentColor = collisionFloat4;
                            bullet.ColorResetTime = collisionColorDuration;
                            bullets[i] = bullet;
                        }
                    }
                }
            }
        }

        void RenderBullets()
        {
            if (activeBulletCount == 0 || bulletMesh == null || bulletMaterial == null) return;

            NativeArray<Matrix4x4>.Copy(matrices, matricesForRender, activeBulletCount);
            NativeArray<Vector4>.Copy(colorArray, colorsForRender, activeBulletCount);

            propertyBlock.SetVectorArray("_BaseColor", colorsForRender);
            Graphics.DrawMeshInstanced(bulletMesh, 0, bulletMaterial, matricesForRender, activeBulletCount, propertyBlock, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }
}
#endif