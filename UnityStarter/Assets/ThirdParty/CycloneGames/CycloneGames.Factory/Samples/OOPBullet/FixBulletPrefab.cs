using UnityEngine;

namespace CycloneGames.Factory.OOPBullet
{
    /// <summary>
    /// Editor script to fix bullet prefab by adding missing Rigidbody component
    /// </summary>
    public class FixBulletPrefab : MonoBehaviour
    {
        [ContextMenu("Fix Bullet Prefab")]
        public void FixBulletPrefabs()
        {
            var bullet = GetComponent<Bullet>();
            if (bullet == null)
            {
                Debug.LogError("FixBulletPrefab: No Bullet component found!");
                return;
            }
            
            // Check if Rigidbody exists
            var rigidbody = GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                // Add Rigidbody component
                rigidbody = gameObject.AddComponent<Rigidbody>();
                Debug.Log("FixBulletPrefab: Added Rigidbody component");
            }
            
            // Configure Rigidbody for bullet behavior
            rigidbody.useGravity = false;
            rigidbody.drag = 0f;
            rigidbody.angularDrag = 0f;
            rigidbody.mass = 0.1f;
            rigidbody.isKinematic = false;
            
            Debug.Log("FixBulletPrefab: Configured Rigidbody for bullet behavior");
            
            // Save the prefab if this is a prefab instance
            #if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                UnityEditor.PrefabUtility.ApplyPrefabInstance(gameObject, UnityEditor.InteractionMode.AutomatedAction);
                Debug.Log("FixBulletPrefab: Applied changes to prefab");
            }
            #endif
        }
        
        private void Start()
        {
            // Automatically fix the prefab when the game starts
            FixBulletPrefabs();
        }
    }
}
