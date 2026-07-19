using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.UIFramework.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Tests.Editor
{
    internal static class TestReflection
    {
        public static void SetField(object target, string fieldName, object value)
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

        public static object Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().FullName, methodName);
            }

            return method.Invoke(target, arguments);
        }
    }

    internal sealed class UIRuntimeTestFixture : IDisposable
    {
        private readonly List<UnityEngine.Object> _ownedAssets = new List<UnityEngine.Object>(16);

        public UIRuntimeTestFixture(string layerName = "Main")
        {
            RootObject = new GameObject("TestUIRoot", typeof(RectTransform), typeof(Canvas));
            RootObject.SetActive(false);
            Root = RootObject.AddComponent<UIRoot>();

            LayerObject = new GameObject(
                "TestUILayer",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            LayerObject.transform.SetParent(RootObject.transform, false);
            Layer = LayerObject.AddComponent<UILayer>();
            TestReflection.SetField(Layer, "layerName", layerName);

            TestReflection.SetField(Root, "rootCanvas", RootObject.GetComponent<Canvas>());
            TestReflection.SetField(Root, "layerList", new List<UILayer> { Layer });

            LayerConfiguration = ScriptableObject.CreateInstance<UILayerConfiguration>();
            TestReflection.SetField(LayerConfiguration, "layerName", layerName);
            _ownedAssets.Add(LayerConfiguration);
            RootObject.SetActive(true);
        }

        public GameObject RootObject { get; }
        public UIRoot Root { get; }
        public GameObject LayerObject { get; }
        public UILayer Layer { get; }
        public UILayerConfiguration LayerConfiguration { get; }

        public UIWindowConfiguration CreateDirectConfiguration(
            string windowId,
            int priority = 0,
            bool sceneBound = false)
        {
            return CreateDirectConfiguration<UIWindow>(windowId, priority, sceneBound);
        }

        public UIWindowConfiguration CreateDirectConfiguration<TWindow>(
            string windowId,
            int priority = 0,
            bool sceneBound = false)
            where TWindow : UIWindow
        {
            GameObject prefabObject = new GameObject(
                windowId + "_Prefab",
                typeof(RectTransform),
                typeof(CanvasGroup));
            UIWindow prefab = prefabObject.AddComponent<TWindow>();
            _ownedAssets.Add(prefabObject);

            UIWindowConfiguration configuration = CreateConfigurationBase(
                windowId,
                UIWindowConfiguration.PrefabSource.PrefabReference,
                priority,
                sceneBound);
            TestReflection.SetField(configuration, "windowPrefab", prefab);
            return configuration;
        }

        public UIWindowConfiguration CreateAddressConfiguration(
            string windowId,
            string location = null,
            int priority = 0,
            bool sceneBound = false)
        {
            UIWindowConfiguration configuration = CreateConfigurationBase(
                windowId,
                UIWindowConfiguration.PrefabSource.AssetReference,
                priority,
                sceneBound);
            TestReflection.SetField(
                configuration,
                "prefabAssetRef",
                new UIAssetReference(location ?? "UI/" + windowId, "editor-guid-" + windowId));
            return configuration;
        }

        public GameObject CreateWindowPrefab(string windowId)
        {
            return CreateWindowPrefab<UIWindow>(windowId);
        }

        public GameObject CreateWindowPrefab<TWindow>(string windowId)
            where TWindow : UIWindow
        {
            GameObject prefabObject = new GameObject(
                windowId + "_ProviderPrefab",
                typeof(RectTransform),
                typeof(CanvasGroup));
            prefabObject.AddComponent<TWindow>();
            _ownedAssets.Add(prefabObject);
            return prefabObject;
        }

        public GameObject CreateInvalidPrefab(string name)
        {
            GameObject prefabObject = new GameObject(name, typeof(RectTransform));
            _ownedAssets.Add(prefabObject);
            return prefabObject;
        }

        public void Dispose()
        {
            if (RootObject != null)
            {
                UnityEngine.Object.DestroyImmediate(RootObject);
            }

            for (int i = _ownedAssets.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object owned = _ownedAssets[i];
                if (owned != null)
                {
                    UnityEngine.Object.DestroyImmediate(owned);
                }
            }

            _ownedAssets.Clear();
        }

        private UIWindowConfiguration CreateConfigurationBase(
            string windowId,
            UIWindowConfiguration.PrefabSource source,
            int priority,
            bool sceneBound)
        {
            UIWindowConfiguration configuration =
                ScriptableObject.CreateInstance<UIWindowConfiguration>();
            TestReflection.SetField(configuration, "windowId", windowId);
            TestReflection.SetField(configuration, "source", source);
            TestReflection.SetField(configuration, "layer", LayerConfiguration);
            TestReflection.SetField(configuration, "priority", priority);
            TestReflection.SetField(configuration, "isSceneBound", sceneBound);
            _ownedAssets.Add(configuration);
            return configuration;
        }
    }

    internal sealed class TestAssetLease<TAsset> : IUIAssetLease<TAsset>
        where TAsset : UnityEngine.Object
    {
        public TestAssetLease(TAsset asset, bool throwOnDispose = false)
        {
            Asset = asset;
            ThrowOnDispose = throwOnDispose;
        }

        public TAsset Asset { get; }
        public int DisposeCount { get; private set; }
        public int DisposeThreadId { get; private set; }
        public bool ThrowOnDispose { get; }

        public void Dispose()
        {
            DisposeCount++;
            DisposeThreadId = Thread.CurrentThread.ManagedThreadId;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("Test lease disposal failed.");
            }
        }
    }

    internal sealed class RecordingAssetProvider : IUIWindowAssetProvider
    {
        public TestAssetLease<UIWindowConfiguration> ConfigurationLease { get; set; }
        public TestAssetLease<GameObject> PrefabLease { get; set; }
        public UniTaskCompletionSource<IUIAssetLease<UIWindowConfiguration>> ConfigurationGate { get; set; }
        public UniTaskCompletionSource<IUIAssetLease<GameObject>> PrefabGate { get; set; }
        public bool IgnoreConfigurationCancellation { get; set; }
        public bool IgnorePrefabCancellation { get; set; }
        public bool ThrowOnCancellation { get; set; }

        public int ConfigurationAcquireCount { get; private set; }
        public int PrefabAcquireCount { get; private set; }
        public string LastWindowId { get; private set; }
        public UIAssetReference LastPrefabReference { get; private set; }
        public UIAssetLoadContext LastConfigurationContext { get; private set; }
        public UIAssetLoadContext LastPrefabContext { get; private set; }

        public UniTask<IUIAssetLease<UIWindowConfiguration>> AcquireConfigurationAsync(
            string windowId,
            UIAssetLoadContext context,
            CancellationToken cancellationToken)
        {
            ConfigurationAcquireCount++;
            LastWindowId = windowId;
            LastConfigurationContext = context;
            if (ThrowOnCancellation)
            {
                cancellationToken.Register(() =>
                    throw new InvalidOperationException("Test cancellation callback failed."));
            }

            if (ConfigurationGate != null)
            {
                return IgnoreConfigurationCancellation
                    ? ConfigurationGate.Task
                    : ConfigurationGate.Task.AttachExternalCancellation(cancellationToken);
            }

            return UniTask.FromResult<IUIAssetLease<UIWindowConfiguration>>(ConfigurationLease);
        }

        public UniTask<IUIAssetLease<GameObject>> AcquirePrefabAsync(
            UIAssetReference reference,
            UIAssetLoadContext context,
            CancellationToken cancellationToken)
        {
            PrefabAcquireCount++;
            LastPrefabReference = reference;
            LastPrefabContext = context;
            if (PrefabGate != null)
            {
                return IgnorePrefabCancellation
                    ? PrefabGate.Task
                    : PrefabGate.Task.AttachExternalCancellation(cancellationToken);
            }

            return UniTask.FromResult<IUIAssetLease<GameObject>>(PrefabLease);
        }
    }

    internal sealed class RecordingWindowBinder : IUIWindowBinder
    {
        private readonly string _name;
        private readonly List<string> _events;
        private readonly bool _throwOnBind;

        public RecordingWindowBinder(string name, List<string> events, bool throwOnBind = false)
        {
            _name = name;
            _events = events;
            _throwOnBind = throwOnBind;
        }

        public int BindCount { get; private set; }

        public IUIWindowBinding Bind(UIWindowBindingContext context)
        {
            BindCount++;
            _events.Add(_name + ":Bind");
            if (_throwOnBind)
            {
                throw new InvalidOperationException(_name + " bind failed.");
            }

            return new RecordingBinding(_name, _events);
        }

        private sealed class RecordingBinding : IUIWindowBinding
        {
            private readonly string _name;
            private readonly List<string> _events;
            private bool _disposed;

            public RecordingBinding(string name, List<string> events)
            {
                _name = name;
                _events = events;
            }

            public void OnWindowStateChanged(WindowStateCallbackType state)
            {
                _events.Add(_name + ":" + state);
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _events.Add(_name + ":Dispose");
            }
        }
    }

    internal sealed class CancelOpenTransitionDriver : IUIWindowTransitionDriver
    {
        public UniTask PlayOpenAsync(UIWindow window, CancellationToken cancellationToken)
        {
            return UniTask.FromCanceled(new CancellationToken(canceled: true));
        }

        public UniTask PlayCloseAsync(UIWindow window, CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }
    }

    internal sealed class RecordingTransitionCoordinator : IUITransitionCoordinator
    {
        private readonly bool _cancel;

        public RecordingTransitionCoordinator(bool cancel = false)
        {
            _cancel = cancel;
        }

        public int CallCount { get; private set; }
        public UIWindow Leaving { get; private set; }
        public UIWindow Entering { get; private set; }
        public NavigationDirection Direction { get; private set; }

        public UniTask TransitionAsync(
            UIWindow leaving,
            UIWindow entering,
            NavigationDirection direction,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Leaving = leaving;
            Entering = entering;
            Direction = direction;
            return _cancel
                ? UniTask.FromCanceled(new CancellationToken(canceled: true))
                : UniTask.CompletedTask;
        }
    }
}
