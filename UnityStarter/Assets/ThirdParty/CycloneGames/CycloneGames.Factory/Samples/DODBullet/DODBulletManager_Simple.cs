using UnityEngine;

namespace CycloneGames.Factory.DODBullet
{
    public class DODBulletManager_Simple : MonoBehaviour
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
        
        private struct Bullet
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float RemainingLifetime;
            public Color CurrentColor;
            public float ColorResetTime;
        }

        private Bullet[] bullets;
        private Matrix4x4[] matrices;
        private Vector4[] colorsForRender;
        private int activeBulletCount = 0;

        private float spawnTimer;
        private float spawnRate;

        void Start()
        {
            bullets = new Bullet[maxBullets];
            matrices = new Matrix4x4[maxBullets];
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

        void Update()
        {
            HandleSpawning();
            UpdateBullets();
            RenderBullets();
        }

        void HandleSpawning()
        {
            spawnTimer += Time.deltaTime;
            while (spawnTimer >= spawnRate)
            {
                spawnTimer -= spawnRate;
                if (activeBulletCount < maxBullets)
                {
                    SpawnBullet();
                }
            }
        }

        void SpawnBullet()
        {
            ref var bullet = ref bullets[activeBulletCount];
            
            float randomX = Random.Range(-spawnAreaSize.x / 2, spawnAreaSize.x / 2);
            float randomY = Random.Range(-spawnAreaSize.y / 2, spawnAreaSize.y / 2);
            float randomZ = Random.Range(-spawnAreaSize.z / 2, spawnAreaSize.z / 2);

            bullet.Position = spawnAreaCenter + new Vector3(randomX, randomY, randomZ);
            bullet.Velocity = defaultVelocity.normalized * bulletSpeed;
            bullet.RemainingLifetime = bulletLifetime;
            bullet.CurrentColor = defaultColor;
            bullet.ColorResetTime = 0;

            activeBulletCount++;
        }

        void UpdateBullets()
        {
            float deltaTime = Time.deltaTime;
            float scaledCollisionRadius = collisionRadius * bulletScale;

            for (int i = activeBulletCount - 1; i >= 0; i--)
            {
                bullets[i].RemainingLifetime -= deltaTime;
                if (bullets[i].RemainingLifetime <= 0)
                {
                    bullets[i] = bullets[activeBulletCount - 1];
                    activeBulletCount--;
                    continue;
                }

                if (bullets[i].ColorResetTime > 0)
                {
                    bullets[i].ColorResetTime -= deltaTime;
                    if (bullets[i].ColorResetTime <= 0)
                    {
                        bullets[i].CurrentColor = defaultColor;
                    }
                }
                
                if (enableHoming && target != null)
                {
                    Vector3 directionToTarget = (target.position - bullets[i].Position).normalized;
                    bullets[i].Velocity = Vector3.Lerp(bullets[i].Velocity.normalized, directionToTarget, homingStrength * deltaTime).normalized * bulletSpeed;
                }
                
                Vector3 oldPosition = bullets[i].Position;
                Vector3 movement = bullets[i].Velocity * deltaTime;
                bullets[i].Position += movement;

                if (collisionLayers.value != 0)
                {
                    bool shouldCheckCollision = (interactionZone == null) || interactionBounds.Contains(bullets[i].Position);
                    if (shouldCheckCollision)
                    {
                        float movementDistance = movement.magnitude;
                        if (movementDistance > 0 && Physics.SphereCast(oldPosition, scaledCollisionRadius, movement.normalized, out RaycastHit hit, movementDistance, collisionLayers))
                        {
                            // UnityEngine.Debug.Log($"Bullet collided with {hit.collider.name}");
                            bullets[i].CurrentColor = collisionColor;
                            bullets[i].ColorResetTime = collisionColorDuration;
                        }
                    }
                }
            }
        }

        void RenderBullets()
        {
            if (activeBulletCount == 0 || bulletMesh == null || bulletMaterial == null) return;

            Vector3 scale = new Vector3(bulletScale, bulletScale, bulletScale);
            for (int i = 0; i < activeBulletCount; i++)
            {
                matrices[i] = Matrix4x4.TRS(bullets[i].Position, Quaternion.identity, scale);
                colorsForRender[i] = bullets[i].CurrentColor;
            }

            propertyBlock.SetVectorArray("_BaseColor", colorsForRender);
            Graphics.DrawMeshInstanced(bulletMesh, 0, bulletMaterial, matrices, activeBulletCount, propertyBlock, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }
}
