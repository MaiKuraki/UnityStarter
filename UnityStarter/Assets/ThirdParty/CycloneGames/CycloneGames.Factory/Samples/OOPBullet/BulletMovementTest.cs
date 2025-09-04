using UnityEngine;

namespace CycloneGames.Factory.OOPBullet
{
    /// <summary>
    /// Simple test script to verify bullet movement
    /// </summary>
    public class BulletMovementTest : MonoBehaviour
    {
        [Header("Test Settings")]
        [SerializeField] private Bullet bulletPrefab;
        [SerializeField] private Vector3 testVelocity = new Vector3(0, 0, 10f);
        [SerializeField] private float testLifetime = 10f;
        
        private Bullet _testBullet;

        private void Start()
        {
            if (bulletPrefab == null)
            {
                Debug.LogError("BulletMovementTest: No bullet prefab assigned!");
                return;
            }
            
            // Create a test bullet
            CreateTestBullet();
        }

        private void CreateTestBullet()
        {
            // Instantiate a bullet directly (not from pool for testing)
            var bulletGO = Instantiate(bulletPrefab.gameObject);
            _testBullet = bulletGO.GetComponent<Bullet>();
            
            if (_testBullet == null)
            {
                Debug.LogError("BulletMovementTest: Bullet component not found on prefab!");
                Destroy(bulletGO);
                return;
            }
            
            // Set position and velocity
            bulletGO.transform.position = Vector3.zero;
            _testBullet.SetPositionAndVelocity(Vector3.zero, testVelocity);
            
            // Manually call OnSpawned to initialize
            var bulletData = new BulletData(testVelocity, testLifetime);
            _testBullet.OnSpawned(bulletData, null);
            
            Debug.Log($"BulletMovementTest: Created test bullet with velocity: {testVelocity}");
        }

        private void Update()
        {
            if (_testBullet != null && _testBullet.IsActive)
            {
                // Call Tick manually since we're not using the pool
                _testBullet.Tick();
                
                // Check if bullet is moving
                var rigidbody = _testBullet.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    if (rigidbody.velocity.magnitude < 0.1f)
                    {
                        Debug.LogWarning("BulletMovementTest: Bullet is not moving! Velocity: " + rigidbody.velocity);
                    }
                }
            }
        }

        private void OnGUI()
        {
            if (_testBullet != null)
            {
                GUILayout.BeginArea(new Rect(10, 10, 300, 150));
                GUILayout.Label("Bullet Movement Test", GUI.skin.box);
                GUILayout.Label($"Position: {_testBullet.transform.position}");
                
                var rigidbody = _testBullet.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    GUILayout.Label($"Velocity: {rigidbody.velocity}");
                    GUILayout.Label($"Speed: {rigidbody.velocity.magnitude:F2}");
                }
                
                GUILayout.Label($"Active: {_testBullet.IsActive}");
                GUILayout.EndArea();
            }
        }
    }
}
