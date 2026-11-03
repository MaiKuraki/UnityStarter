using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Tests.PlayMode
{
    /// <summary>
    /// Verifies that a window destroyed at runtime still releases its session, and logs which
    /// destruction path fired. Fails only if the service does not converge.
    /// </summary>
    public sealed class UIFrameworkLifetimeDiagnosticsTests
    {
        [UnityTest]
        public IEnumerator DestroyAtRuntime_PublishesNotificationPathAndConverges()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var owned = new List<UnityEngine.Object>(8);
                UIService service = null;
                try
                {
                    UIRoot root = CreateRoot(owned);
                    UIWindowConfiguration configuration = CreateConfiguration(root, owned);
                    service = new UIService(root);

                    UIWindow window = await service.OpenAsync(configuration);

                    Component relay = FindComponent(window.gameObject, "UIWindowLifetimeRelay");
                    Type relayType = relay != null ? relay.GetType() : null;
                    FieldInfo relayNotified = relayType != null
                        ? relayType.GetField("_notified", BindingFlags.Instance | BindingFlags.NonPublic)
                        : null;
                    FieldInfo windowNotified = typeof(UIWindow).GetField(
                        "_destroyNotified", BindingFlags.Instance | BindingFlags.NonPublic);

                    string stateBefore = window.State.ToString();
                    bool activeBefore = window.gameObject.activeInHierarchy;

                    UnityEngine.Object.Destroy(window.gameObject);
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    await UniTask.Yield(PlayerLoopTiming.Update);

                    bool relayFlag = relayNotified != null && relay != null &&
                                     (bool)relayNotified.GetValue(relay);
                    bool windowFlag = windowNotified != null && (bool)windowNotified.GetValue(window);
                    string stateAfter = ReferenceEquals(window, null) ? "<raw-null>" : window.State.ToString();

                    UnityEngine.Debug.Log(
                        "PMIDIAG,destroyAtRuntime" +
                        ",relayFound=" + (relay != null) +
                        ",activeBefore=" + activeBefore +
                        ",stateBefore=" + stateBefore +
                        ",stateAfter=" + stateAfter +
                        ",relayNotified=" + relayFlag +
                        ",windowNotified=" + windowFlag +
                        ",activeCountRaw=" + service.ActiveWindowCount +
                        ",sessionCount=" + service.GetPerformanceStats().SessionCount);

                    Assert.That(
                        service.ActiveWindowCount,
                        Is.EqualTo(0),
                        "the service must converge after a runtime destroy regardless of whether " +
                        "Unity delivered OnDestroy (see the PMIDIAG line in the log)");
                    Assert.That(
                        service.GetPerformanceStats().SessionCount,
                        Is.EqualTo(0),
                        "the session must be released after a runtime destroy");
                }
                finally
                {
                    service?.Dispose();
                    for (int i = owned.Count - 1; i >= 0; i--)
                    {
                        if (owned[i] != null)
                        {
                            UnityEngine.Object.Destroy(owned[i]);
                        }
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            });
        }

        private static Component FindComponent(GameObject target, string typeName)
        {
            Component[] components = target.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null &&
                    string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
                {
                    return component;
                }
            }

            return null;
        }

        private static UIRoot CreateRoot(List<UnityEngine.Object> owned)
        {
            var rootObject = new GameObject("PMIDIAGRoot", typeof(RectTransform), typeof(Canvas));
            rootObject.SetActive(false);
            owned.Add(rootObject);
            UIRoot root = rootObject.AddComponent<UIRoot>();

            var layerObject = new GameObject(
                "PMIDIAGLayer",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            layerObject.transform.SetParent(rootObject.transform, false);
            UILayer layer = layerObject.AddComponent<UILayer>();
            SetField(layer, "layerName", "Main");

            SetField(root, "rootCanvas", rootObject.GetComponent<Canvas>());
            SetField(root, "layerList", new List<UILayer> { layer });
            rootObject.SetActive(true);
            return root;
        }

        private static UIWindowConfiguration CreateConfiguration(UIRoot root, List<UnityEngine.Object> owned)
        {
            var layerConfiguration = ScriptableObject.CreateInstance<UILayerConfiguration>();
            SetField(layerConfiguration, "layerName", "Main");
            owned.Add(layerConfiguration);

            var prefabObject = new GameObject(
                "PMIDIAGWindowPrefab",
                typeof(RectTransform),
                typeof(CanvasGroup));
            UIWindow prefab = prefabObject.AddComponent<UIWindow>();
            owned.Add(prefabObject);

            UIWindowConfiguration configuration =
                ScriptableObject.CreateInstance<UIWindowConfiguration>();
            SetField(configuration, "windowId", "PMIDIAGWindow");
            SetField(configuration, "source", UIWindowConfiguration.PrefabSource.PrefabReference);
            SetField(configuration, "windowPrefab", prefab);
            SetField(configuration, "layer", layerConfiguration);
            owned.Add(configuration);
            return configuration;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }

                type = type.BaseType;
            }

            throw new MissingFieldException(target.GetType().FullName, fieldName);
        }
    }
}
