using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Tests.PlayMode
{
    public sealed class UIServicePlayModeTests
    {
        private sealed class CountingBinder : IUIWindowBinder
        {
            public int DisposeCount { get; private set; }

            public IUIWindowBinding Bind(UIWindowBindingContext context)
            {
                return new Binding(this);
            }

            private sealed class Binding : IUIWindowBinding
            {
                private CountingBinder _owner;

                public Binding(CountingBinder owner)
                {
                    _owner = owner;
                }

                public void OnWindowStateChanged(WindowStateCallbackType state) { }

                public void Dispose()
                {
                    CountingBinder owner = _owner;
                    if (owner == null)
                    {
                        return;
                    }

                    _owner = null;
                    owner.DisposeCount++;
                }
            }
        }

        private sealed class GatedCloseTransitionDriver : IUIWindowTransitionDriver
        {
            private readonly UniTaskCompletionSource _gate = new UniTaskCompletionSource();

            public CancellationToken ObservedCloseToken { get; private set; }

            public UniTask PlayOpenAsync(UIWindow window, CancellationToken cancellationToken)
            {
                return UniTask.CompletedTask;
            }

            public UniTask PlayCloseAsync(UIWindow window, CancellationToken cancellationToken)
            {
                ObservedCloseToken = cancellationToken;
                return _gate.Task.AttachExternalCancellation(cancellationToken);
            }

            public void Release() => _gate.TrySetResult();
        }

        [UnityTest]
        public IEnumerator SceneBoundWindow_ClosesWhenActiveSceneChanges()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var owned = new List<UnityEngine.Object>(8);
                UIService service = null;
                Scene originalScene = SceneManager.GetActiveScene();
                Scene temporaryScene = default;

                try
                {
                    UIRoot root = CreateRoot(owned, out UILayerConfiguration layerConfiguration);
                    UIWindowConfiguration configuration = CreateConfiguration(
                        "SceneWindow",
                        layerConfiguration,
                        sceneBound: true,
                        owned);
                    service = new UIService(root);

                    UIWindow window = await service.OpenAsync(configuration);
                    Assert.AreEqual(1, service.ActiveWindowCount);
                    Assert.IsTrue(window.IsSceneBound);
                    Assert.AreEqual(originalScene.handle, window.BoundSceneHandle);

                    temporaryScene = SceneManager.CreateScene(
                        "UIFramework_PlayMode_" + Guid.NewGuid().ToString("N"));
                    Assert.IsTrue(SceneManager.SetActiveScene(temporaryScene));
                    await UniTask.Yield(PlayerLoopTiming.Update);

                    Assert.AreEqual(0, service.ActiveWindowCount);
                    Assert.IsFalse(service.TryGetWindow("SceneWindow", out _));
                }
                finally
                {
                    service?.Dispose();

                    if (originalScene.IsValid() && originalScene.isLoaded)
                    {
                        SceneManager.SetActiveScene(originalScene);
                    }

                    if (temporaryScene.IsValid() && temporaryScene.isLoaded)
                    {
                        AsyncOperation unload = SceneManager.UnloadSceneAsync(temporaryScene);
                        while (unload != null && !unload.isDone)
                        {
                            await UniTask.Yield(PlayerLoopTiming.Update);
                        }
                    }

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

        [UnityTest]
        public IEnumerator ExternalDestroy_RemovesSessionAndDisposesBinding()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var owned = new List<UnityEngine.Object>(8);
                UIService service = null;

                try
                {
                    UIRoot root = CreateRoot(owned, out UILayerConfiguration layerConfiguration);
                    UIWindowConfiguration configuration = CreateConfiguration(
                        "ExternalDestroyWindow",
                        layerConfiguration,
                        sceneBound: false,
                        owned);
                    var binder = new CountingBinder();
                    service = new UIService(
                        root,
                        assetProvider: null,
                        options: null,
                        binders: new IUIWindowBinder[] { binder });
                    UIWindow window = await service.OpenAsync(configuration);

                    UnityEngine.Object.Destroy(window.gameObject);
                    await UniTask.Yield(PlayerLoopTiming.Update);

                    Assert.AreEqual(0, service.ActiveWindowCount);
                    Assert.IsFalse(service.TryGetWindow("ExternalDestroyWindow", out _));
                    Assert.AreEqual(1, binder.DisposeCount);
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

        [UnityTest]
        public IEnumerator ExternalDestroyDuringClose_CancelsTargetLifetimeTransition()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var owned = new List<UnityEngine.Object>(8);
                UIService service = null;
                var transition = new GatedCloseTransitionDriver();

                try
                {
                    UIRoot root = CreateRoot(owned, out UILayerConfiguration layerConfiguration);
                    UIWindowConfiguration configuration = CreateConfiguration(
                        "DestroyDuringClose",
                        layerConfiguration,
                        sceneBound: false,
                        owned);
                    service = new UIService(
                        root,
                        options: new UIServiceOptions
                        {
                            DefaultTransitionDriver = transition,
                        });
                    UIWindow window = await service.OpenAsync(configuration);
                    UniTask<bool> close = service.CloseAsync("DestroyDuringClose");
                    Assert.AreEqual(UniTaskStatus.Pending, close.Status);

                    UnityEngine.Object.Destroy(window.gameObject);
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    Assert.IsTrue(await close);

                    Assert.IsTrue(transition.ObservedCloseToken.IsCancellationRequested);
                    Assert.AreEqual(0, service.ActiveWindowCount);
                    Assert.IsFalse(service.TryGetWindow("DestroyDuringClose", out _));
                }
                finally
                {
                    transition.Release();
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

        private static UIRoot CreateRoot(
            List<UnityEngine.Object> owned,
            out UILayerConfiguration layerConfiguration)
        {
            var rootObject = new GameObject(
                "PlayModeUIRoot",
                typeof(RectTransform),
                typeof(Canvas));
            rootObject.SetActive(false);
            owned.Add(rootObject);
            UIRoot root = rootObject.AddComponent<UIRoot>();

            var layerObject = new GameObject(
                "PlayModeUILayer",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            layerObject.transform.SetParent(rootObject.transform, false);
            UILayer layer = layerObject.AddComponent<UILayer>();
            SetField(layer, "layerName", "Main");

            SetField(root, "rootCanvas", rootObject.GetComponent<Canvas>());
            SetField(root, "layerList", new List<UILayer> { layer });

            layerConfiguration = ScriptableObject.CreateInstance<UILayerConfiguration>();
            SetField(layerConfiguration, "layerName", "Main");
            owned.Add(layerConfiguration);
            rootObject.SetActive(true);
            return root;
        }

        private static UIWindowConfiguration CreateConfiguration(
            string windowId,
            UILayerConfiguration layer,
            bool sceneBound,
            List<UnityEngine.Object> owned)
        {
            var prefabObject = new GameObject(
                windowId + "_Prefab",
                typeof(RectTransform),
                typeof(CanvasGroup));
            UIWindow prefab = prefabObject.AddComponent<UIWindow>();
            owned.Add(prefabObject);

            UIWindowConfiguration configuration =
                ScriptableObject.CreateInstance<UIWindowConfiguration>();
            SetField(configuration, "windowId", windowId);
            SetField(
                configuration,
                "source",
                UIWindowConfiguration.PrefabSource.PrefabReference);
            SetField(configuration, "windowPrefab", prefab);
            SetField(configuration, "layer", layer);
            SetField(configuration, "isSceneBound", sceneBound);
            owned.Add(configuration);
            return configuration;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }
    }
}
