using System;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class PawnConfigContractTests
    {
        private GameObject pawnObject;
        private PawnConfig config;

        [TearDown]
        public void TearDown()
        {
            if (pawnObject != null)
            {
                Object.DestroyImmediate(pawnObject);
            }

            if (config != null)
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void SetPawnConfig_AppliesOneValidatedAuthoringSource()
        {
            pawnObject = new GameObject("ConfiguredPawn");
            Pawn pawn = pawnObject.AddComponent<Pawn>();
            UnityLifecycleTestUtility.InvokeAwake(pawn);
            config = ScriptableObject.CreateInstance<PawnConfig>();
            var serializedConfig = new SerializedObject(config);
            serializedConfig.FindProperty("useControllerRotationPitch").boolValue = true;
            serializedConfig.FindProperty("useControllerRotationYaw").boolValue = false;
            serializedConfig.FindProperty("useControllerRotationRoll").boolValue = true;
            serializedConfig.FindProperty("baseEyeHeight").floatValue = 1.75f;
            serializedConfig.FindProperty("maxLookUpAngle").floatValue = 70f;
            serializedConfig.FindProperty("maxLookDownAngle").floatValue = 55f;
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();

            pawn.SetPawnConfig(config);

            Assert.AreSame(config, pawn.GetPawnConfig());
            Assert.IsTrue(pawn.UseControllerRotationPitch);
            Assert.IsFalse(pawn.UseControllerRotationYaw);
            Assert.IsTrue(pawn.UseControllerRotationRoll);
            Assert.AreEqual(1.75f, pawn.BaseEyeHeight);
            Assert.AreEqual(70f, pawn.MaxLookUpAngle);
            Assert.AreEqual(55f, pawn.MaxLookDownAngle);
            Assert.Throws<ArgumentNullException>(() => pawn.SetPawnConfig(null));
        }

        [Test]
        public void SetPawnConfig_InvalidAssetDataRejectsWithoutMutation()
        {
            pawnObject = new GameObject("ConfiguredPawn");
            Pawn pawn = pawnObject.AddComponent<Pawn>();
            UnityLifecycleTestUtility.InvokeAwake(pawn);
            config = ScriptableObject.CreateInstance<PawnConfig>();
            var serializedConfig = new SerializedObject(config);
            serializedConfig.FindProperty("maxLookUpAngle").floatValue = float.NaN;
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();

            Assert.Throws<InvalidOperationException>(() => pawn.SetPawnConfig(config));
            Assert.IsNull(pawn.GetPawnConfig());
            Assert.AreEqual(89f, pawn.MaxLookUpAngle);
        }
    }
}
