using Microsoft.CodeAnalysis;

namespace CycloneGames.Analyzers
{
    public static class DiagnosticRules
    {
        public static readonly DiagnosticDescriptor HotPathForEach = new(
            DiagnosticIds.HotPathForEach,
            "Hot path: foreach may allocate",
            "Method '{0}' is in a hot path. 'foreach' over '{1}' may allocate an enumerator in IL2CPP builds. Use 'for' with index.",
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotPathLinq = new(
            DiagnosticIds.HotPathLinq,
            "Hot path: LINQ causes hidden allocations",
            "Method '{0}' is in a hot path. LINQ method '{1}' allocates temporary enumerables or delegates.",
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotPathStringConcat = new(
            DiagnosticIds.HotPathStringConcat,
            "Hot path: string construction causes allocation",
            "Method '{0}' is in a hot path. String interpolation, formatting, or concatenation allocates. Use cached or pooled text.",
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CameraMainInHotPath = new(
            DiagnosticIds.CameraMainInHotPath,
            "Camera.main in hot path is a scene search",
            "'Camera.main' in method '{0}' is a hidden FindGameObjectWithTag call. Cache the Camera reference outside the hot path.",
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor GameObjectFind = new(
            DiagnosticIds.GameObjectFind,
            "GameObject.Find is forbidden in production code",
            "'GameObject.Find({0})' is fragile and slow. Use a serialized reference, DI, or an explicit registry.",
            DiagnosticCategories.Safety,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor FindObjectOfType = new(
            DiagnosticIds.FindObjectOfType,
            "Scene-wide find API is forbidden in production code",
            "'{0}' is an O(n) scene-wide search. Use a serialized reference, DI, or an explicit registry.",
            DiagnosticCategories.Safety,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SendMessageApi = new(
            DiagnosticIds.SendMessageApi,
            "SendMessage and BroadcastMessage are forbidden",
            "'{0}' is slow, string-based, and has no compile-time safety.",
            DiagnosticCategories.Safety,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvokeApi = new(
            DiagnosticIds.InvokeApi,
            "MonoBehaviour.Invoke APIs are forbidden",
            "'{0}' uses string-based method lookup. Use UniTask.Delay or an explicit timer component.",
            DiagnosticCategories.Safety,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ResourcesLoad = new(
            DiagnosticIds.ResourcesLoad,
            "Resources.Load bypasses the asset pipeline",
            "'Resources.Load' bypasses the project asset management pipeline. Use Addressables, AssetReference, or IAssetProvider.",
            DiagnosticCategories.Safety,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ActorStartBaseCall = new(
            DiagnosticIds.ActorStartBaseCall,
            "Actor.Start override must call base.Start()",
            "'{0}.Start()' overrides Actor.Start() without calling base.Start(). BeginPlay() will not fire.",
            DiagnosticCategories.ApiContract,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PoolOnDespawnOverride = new(
            DiagnosticIds.PoolOnDespawnOverride,
            "FastObjectPool subclass must override OnDespawn",
            "'{0}' extends FastObjectPool<{1}> but does not override OnDespawn. Pooled objects may leak state on return.",
            DiagnosticCategories.ApiContract,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor GameplayTagImplicitCast = new(
            DiagnosticIds.GameplayTagImplicitCast,
            "GameplayTag implicit string cast should be validated",
            "Implicit cast from string '{0}' returns an invalid tag if unregistered. Use TryRequestTag or check IsValid before use.",
            DiagnosticCategories.ApiContract,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PublicFieldOnMonoBehaviour = new(
            DiagnosticIds.PublicFieldOnMonoBehaviour,
            "MonoBehaviour should not expose public fields",
            "Field '{0}' is public on a MonoBehaviour subclass. Use [SerializeField] private plus a property instead.",
            DiagnosticCategories.Convention,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UsingStaticDirective = new(
            DiagnosticIds.UsingStaticDirective,
            "using static directive is not allowed in Runtime code",
            "'using static {0}' reduces code clarity. Use explicit type qualification.",
            DiagnosticCategories.Convention,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RegionInRuntime = new(
            DiagnosticIds.RegionInRuntime,
            "#region is discouraged in Runtime code",
            "#region in Runtime code often hides poor organization. Prefer smaller types or partial classes.",
            DiagnosticCategories.Convention,
            DiagnosticSeverity.Info,
            isEnabledByDefault: false);

        public static readonly DiagnosticDescriptor ObsoleteInFramework = new(
            DiagnosticIds.ObsoleteInFramework,
            "[Obsolete] is forbidden in framework assemblies",
            "[Obsolete] should not be used in CycloneGames framework code. Delete the API and update callers directly.",
            DiagnosticCategories.Convention,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AsyncVoidInRuntime = new(
            DiagnosticIds.AsyncVoidInRuntime,
            "async void in Runtime code is unsafe",
            "Method '{0}' is async void in Runtime code. Use async UniTask or async Task unless this is a required event handler.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TransformChainAccess = new(
            DiagnosticIds.TransformChainAccess,
            "Component.transform chain access in hot path",
            "'{0}.transform' in method '{1}' is a native interop call. Cache the Transform reference outside the hot path.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnityEditorInRuntime = new(
            DiagnosticIds.UnityEditorInRuntime,
            "UnityEditor namespace used outside Editor folders",
            "'{0}' uses UnityEditor namespace outside an Editor folder. Move it to Editor code or wrap it in UNITY_EDITOR.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DebugLogInHotPath = new(
            DiagnosticIds.DebugLogInHotPath,
            "Debug.Log in hot path",
            "Method '{0}' is a hot path. Debug.Log allocates and should not run in per-frame code.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor GetComponentInHotPath = new(
            DiagnosticIds.GetComponentInHotPath,
            "GetComponent<T> in hot path should be cached",
            "'GetComponent<{0}>()' in method '{1}' is an expensive native interop call. Cache the result outside the hot path.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor BoxingInHotPath = new(
            DiagnosticIds.BoxingInHotPath,
            "Possible boxing allocation in hot path",
            "Assigning a value type to object or an interface in method '{0}' may cause boxing allocation.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor LambdaAllocInHotPath = new(
            DiagnosticIds.LambdaAllocInHotPath,
            "Lambda allocation in hot path",
            "Lambda expression or anonymous method in method '{0}' may allocate a delegate. Cache it or use a static method.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AsyncTaskOverUniTask = new(
            DiagnosticIds.AsyncTaskOverUniTask,
            "async Task should be async UniTask when UniTask is available",
            "'{0}' returns Task in a project that references UniTask. Use UniTask for project async workflows.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor StaticClassCircularDependency = new(
            DiagnosticIds.StaticClassCircularDependencyRuleId,
            "Static class circular dependency risk",
            "Static class '{0}' is part of a circular call dependency chain. Break the cycle by extracting shared logic into a non-static helper. Cycle: {1}",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
