using UnityEngine;
using UnityEditor;
using CycloneGames.Utility.Runtime;

namespace CycloneGames.Utility.Editor
{
    public class MonoSingletonTest
    {
        // Test class
        public class TestMonoManager : MonoSingleton<TestMonoManager>
        {
            public int Value = 0;

            // Optional: Override IsGlobal to false if you want scene-specific singleton
            // protected override bool IsGlobal => false; 
        }

        // [MenuItem("Tools/Singleton/Test MonoSingleton")]             //  Un-Comment if you want to run test
        public static void RunTests()
        {
            UnityEngine.Debug.Log("Starting MonoSingleton Tests...");

            // Test 1: Lazy Creation
            var instance1 = TestMonoManager.Instance;
            if (instance1 != null)
            {
                UnityEngine.Debug.Log("[Pass] MonoSingleton Lazy Creation");
            }
            else
            {
                UnityEngine.Debug.LogError("[Fail] MonoSingleton Lazy Creation");
                return;
            }

            // Test 2: Uniqueness
            var instance2 = TestMonoManager.Instance;
            if (ReferenceEquals(instance1, instance2))
            {
                UnityEngine.Debug.Log("[Pass] MonoSingleton Uniqueness");
            }
            else
            {
                UnityEngine.Debug.LogError("[Fail] MonoSingleton Uniqueness");
            }

            // Test 3: Persistence (Check DontDestroyOnLoad)
            if (instance1.gameObject.scene.name == "DontDestroyOnLoad")
            {
                UnityEngine.Debug.Log("[Pass] MonoSingleton Persistence (DontDestroyOnLoad)");
            }
            else
            {
                // Note: In Editor mode, DontDestroyOnLoad might not be immediately visible in scene struct without play mode,
                // but the scene name should be correct if it worked. 
                // However, DontDestroyOnLoad only works in Play Mode. 
                // In Edit Mode, it stays in the active scene.
                if (Application.isPlaying)
                {
                    UnityEngine.Debug.LogError($"[Fail] MonoSingleton Persistence. Scene: {instance1.gameObject.scene.name}");
                }
                else
                {
                    UnityEngine.Debug.Log("[Info] MonoSingleton Persistence check skipped (Not in Play Mode)");
                }
            }

            // Cleanup
            if (!Application.isPlaying)
            {
                if (instance1 != null)
                {
                    Object.DestroyImmediate(instance1.gameObject);
                    UnityEngine.Debug.Log("[Info] Cleaned up MonoSingleton instance (Edit Mode)");
                }
            }
        }
    }
}