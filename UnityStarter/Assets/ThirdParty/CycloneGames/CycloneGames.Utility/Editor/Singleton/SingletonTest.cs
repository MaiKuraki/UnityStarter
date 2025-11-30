using UnityEditor;
using System.Diagnostics;
using CycloneGames.Utility.Runtime;

namespace CycloneGames.Utility.Editor
{
    public class SingletonTest
    {
        // Test classes
        public class TestSingleton : Singleton<TestSingleton>
        {
            public int Value = 0;
        }

        // [MenuItem("Tools/Singleton/Test Singleton")]             //  Un-Comment if you want to run test
        public static void RunTests()
        {
            UnityEngine.Debug.Log("Starting Singleton Tests...");

            // Test 1: Singleton Uniqueness
            var instance1 = TestSingleton.Instance;
            var instance2 = TestSingleton.Instance;
            if (ReferenceEquals(instance1, instance2))
            {
                UnityEngine.Debug.Log("[Pass] Singleton Uniqueness");
            }
            else
            {
                UnityEngine.Debug.LogError("[Fail] Singleton Uniqueness");
            }

            // Test 2: Performance
            int iterations = 10000000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var s = TestSingleton.Instance;
                s.Value++;
            }
            sw.Stop();
            UnityEngine.Debug.Log($"[Performance] Singleton Access ({iterations} times): {sw.ElapsedMilliseconds}ms");
        }
    }
}