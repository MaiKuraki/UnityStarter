using System.Collections;
using System.Reflection;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.PlayMode
{
    public sealed class PawnConfigPlayModeTests
    {
        [UnityTest]
        public IEnumerator SerializedPawnConfig_IsAppliedBeforeFirstActiveFrame()
        {
            PawnConfig config = ScriptableObject.CreateInstance<PawnConfig>();
            GameObject authoringObject = new GameObject("ConfiguredPawnAuthoring");
            GameObject instance = null;
            try
            {
                SetField(config, "useControllerRotationYaw", true);
                SetField(config, "baseEyeHeight", 1.6f);
                SetField(config, "maxLookUpAngle", 65f);
                SetField(config, "maxLookDownAngle", 50f);

                authoringObject.SetActive(false);
                Pawn authoringPawn = authoringObject.AddComponent<Pawn>();
                SetField(authoringPawn, "pawnConfig", config);

                instance = Object.Instantiate(authoringObject);
                instance.name = "ConfiguredPawnInstance";
                instance.SetActive(true);
                yield return null;

                Pawn pawn = instance.GetComponent<Pawn>();
                Assert.AreSame(config, pawn.GetPawnConfig());
                Assert.IsTrue(pawn.UseControllerRotationYaw);
                Assert.AreEqual(1.6f, pawn.BaseEyeHeight);
                Assert.AreEqual(65f, pawn.MaxLookUpAngle);
                Assert.AreEqual(50f, pawn.MaxLookDownAngle);
            }
            finally
            {
                if (instance != null)
                {
                    Object.Destroy(instance);
                }

                Object.Destroy(authoringObject);
                Object.Destroy(config);
            }
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field '{fieldName}' was not found.");
            field.SetValue(target, value);
        }
    }
}
