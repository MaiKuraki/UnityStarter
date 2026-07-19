using System;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

using CycloneGames.AssetManagement.Runtime.CacheRetention;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetCacheRetentionBehaviourTests
    {
        private const double MAXIMUM_RETENTION_SECONDS = 365d * 24d * 60d * 60d;

        [Test]
        public void OnValidate_Clamps_Finite_Values_That_Would_Overflow_TimeSpan()
        {
            var gameObject = new GameObject("AssetCacheRetentionBehaviourTests");
            try
            {
                var behaviour = gameObject.AddComponent<AssetCacheRetentionBehaviour>();
                var serializedObject = new SerializedObject(behaviour);
                serializedObject.FindProperty("MinimumIdleSeconds").doubleValue = double.MaxValue;
                serializedObject.FindProperty("CheckIntervalSeconds").doubleValue = double.MaxValue;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                MethodInfo onValidate = typeof(AssetCacheRetentionBehaviour).GetMethod(
                    "OnValidate",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(onValidate);
                Assert.DoesNotThrow(() => onValidate.Invoke(behaviour, null));

                serializedObject.Update();
                double idleSeconds = serializedObject.FindProperty("MinimumIdleSeconds").doubleValue;
                double intervalSeconds = serializedObject.FindProperty("CheckIntervalSeconds").doubleValue;
                Assert.AreEqual(MAXIMUM_RETENTION_SECONDS, idleSeconds);
                Assert.AreEqual(MAXIMUM_RETENTION_SECONDS, intervalSeconds);
                Assert.DoesNotThrow(() => TimeSpan.FromSeconds(idleSeconds));
                Assert.DoesNotThrow(() => TimeSpan.FromSeconds(intervalSeconds));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }
}
