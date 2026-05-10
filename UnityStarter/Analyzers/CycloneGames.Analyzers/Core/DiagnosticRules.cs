using Microsoft.CodeAnalysis;

namespace CycloneGames.Analyzers
{
    public static class DiagnosticRules
    {
        // ── Performance ──
        public static readonly DiagnosticDescriptor HotPathForEach = new(
            DiagnosticIds.HotPathForEach,
            "Hot path: foreach causes IL2CPP enumerator allocation",
            "Method '{0}' is in a hot path (Update/LateUpdate/FixedUpdate/Tick). " +
            "'foreach' over '{1}' may allocate an enumerator in IL2CPP builds. Use 'for' with index.",
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotPathLinq = new(
            DiagnosticIds.HotPathLinq,
            "Hot path: LINQ causes hidden allocations",
            "Method '{0}' is in a hot path. LINQ method '{1}' allocates temporary enumerables/delegates.",
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotPathStringConcat = new(
            DiagnosticIds.HotPathStringConcat,
            "Hot path: string concatenation causes allocation",
            "Method '{0}' is in a hot path. String interpolation or concatenation allocates. " +
            "Use pooled StringBuilder or pre-computed strings.",
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CameraMainInHotPath = new(
            DiagnosticIds.CameraMainInHotPath,
            "Camera.main in hot path: O(n) FindGameObjectWithTag per call",
            "'Camera.main' in method '{0}' is a hidden FindGameObjectWithTag call. " +
            "Cache in Awake and reuse the cached reference.",
            DiagnosticCategories.Performance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // ── Safety ──
        public static readonly DiagnosticDescriptor GameObjectFind = new(
            DiagnosticIds.GameObjectFind,
            "GameObject.Find is forbidden in production code",
            "'GameObject.Find(\"{0}\")' is fragile and slow. Use [SerializeField] reference, DI, or Service Locator.",
            DiagnosticCategories.Safety,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor FindObjectOfType = new(
            DiagnosticIds.FindObjectOfType,
            "FindObjectOfType is forbidden in production code",
            "'{0}' is an O(n) scene-wide search. Use [SerializeField] reference or DI injection.",
            DiagnosticCategories.Safety,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SendMessageApi = new(
            DiagnosticIds.SendMessageApi,
            "SendMessage/BroadcastMessage is forbidden",
            "'{0}' is slow, error-prone (string-based), and has no compile-time safety.",
            DiagnosticCategories.Safety,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvokeApi = new(
            DiagnosticIds.InvokeApi,
            "MonoBehaviour.Invoke/InvokeRepeating is forbidden",
            "'{0}' uses string-based method lookup. Use UniTask.Delay or TimerComponent instead.",
            DiagnosticCategories.Safety,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ResourcesLoad = new(
            DiagnosticIds.ResourcesLoad,
            "Resources.Load bypasses asset management pipeline",
            "'Resources.Load' in production code. Use Addressables, AssetReference, or IAssetProvider.",
            DiagnosticCategories.Safety,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // ── ApiContract ──
        public static readonly DiagnosticDescriptor NetworkVariableBlittability = new(
            DiagnosticIds.NetworkVariableBlittability,
            "NetworkVariable<T> T must be fully blittable (no reference fields)",
            "Type '{0}' used in NetworkVariable<T> contains reference-type fields. " +
            "Only unmanaged, blittable types are safe for direct memory copy.",
            DiagnosticCategories.ApiContract,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RpcMethodSignature = new(
            DiagnosticIds.RpcMethodSignature,
            "[ServerRpc]/[ClientRpc]/[NetworkRpc] method signature violation",
            "{0}",
            DiagnosticCategories.ApiContract,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ActorStartBaseCall = new(
            DiagnosticIds.ActorStartBaseCall,
            "Actor.Start override must call base.Start()",
            "'{0}.Start()' overrides Actor.Start() without calling base.Start(). " +
            "BeginPlay() will never fire, breaking the Actor lifecycle.",
            DiagnosticCategories.ApiContract,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PoolOnDespawnOverride = new(
            DiagnosticIds.PoolOnDespawnOverride,
            "FastObjectPool subclass must override OnDespawn",
            "'{0}' extends FastObjectPool<{1}> but does not override OnDespawn. " +
            "Pooled objects may leak state on return.",
            DiagnosticCategories.ApiContract,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor GameplayTagImplicitCast = new(
            DiagnosticIds.GameplayTagImplicitCast,
            "GameplayTag implicit cast from string should be validated",
            "Implicit cast from string '{0}' returns an invalid tag if unregistered. " +
            "Use TryRequestTag or check IsValid before use.",
            DiagnosticCategories.ApiContract,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // ── Convention ──
        public static readonly DiagnosticDescriptor PublicFieldOnMonoBehaviour = new(
            DiagnosticIds.PublicFieldOnMonoBehaviour,
            "MonoBehaviour should not expose public fields",
            "Field '{0}' is public on a MonoBehaviour subclass. " +
            "Use [SerializeField] private + property instead.",
            DiagnosticCategories.Convention,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UsingStaticDirective = new(
            DiagnosticIds.UsingStaticDirective,
            "using static directive is not allowed in Runtime assemblies",
            "'using static {0}' reduces code clarity. Use explicit type qualification.",
            DiagnosticCategories.Convention,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RegionInRuntime = new(
            DiagnosticIds.RegionInRuntime,
            "#region is discouraged in Runtime code",
            "#region in Runtime code. Regions often mask poor organization. Extract to partial classes instead.",
            DiagnosticCategories.Convention,
            DiagnosticSeverity.Info,
            isEnabledByDefault: false);

        public static readonly DiagnosticDescriptor ObsoleteInFramework = new(
            DiagnosticIds.ObsoleteInFramework,
            "[Obsolete] is forbidden in framework assemblies",
            "[Obsolete] should not be used in CycloneGames framework. Delete the API and update callers directly.",
            DiagnosticCategories.Convention,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // ── Unity Best Practices ──
        public static readonly DiagnosticDescriptor AsyncVoidInRuntime = new(
            DiagnosticIds.AsyncVoidInRuntime,
            "async void in Runtime code — crash risk from unhandled exceptions",
            "Method '{0}' is async void in Runtime code. Unhandled exceptions crash the application. Use async UniTask or async Task instead.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TransformChainAccess = new(
            DiagnosticIds.TransformChainAccess,
            "Component.transform chain access in hot path",
            "'{0}.transform' in method '{1}' is a native interop call. Cache the Transform reference in Awake.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnityEditorInRuntime = new(
            DiagnosticIds.UnityEditorInRuntime,
            "UnityEditor namespace used outside Editor folder",
            "'{0}' uses UnityEditor namespace but lives in a Runtime folder. Move to Editor folder or wrap in #if UNITY_EDITOR.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DebugLogInHotPath = new(
            DiagnosticIds.DebugLogInHotPath,
            "Debug.Log in hot path — ships to production builds",
            "Method '{0}' is a hot path. 'Debug.Log' allocates GC and is not stripped from builds. Remove or guard with #if UNITY_EDITOR.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor GetComponentInHotPath = new(
            DiagnosticIds.GetComponentInHotPath,
            "GetComponent<T> in hot path — should be cached in Awake",
            "'GetComponent<{0}>()' in method '{1}' is an expensive native interop call. Cache the result in Awake.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor BoxingInHotPath = new(
            DiagnosticIds.BoxingInHotPath,
            "Possible boxing allocation in hot path",
            "Assigning value type to interface/object in method '{0}' may cause boxing allocation.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor LambdaAllocInHotPath = new(
            DiagnosticIds.LambdaAllocInHotPath,
            "Lambda allocation in hot path",
            "Lambda expression or closure capture in method '{0}' allocates a delegate. Cache or use a static method instead.",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AsyncTaskOverUniTask = new(
            DiagnosticIds.AsyncTaskOverUniTask,
            "async Task should be async UniTask when UniTask is available",
            "'{0}' returns Task in a project that references UniTask. " +
            "Use 'UniTask' (struct, poolable) instead of 'Task' (class, always allocates).",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor StaticClassCircularDependency = new(
            DiagnosticIds.StaticClassCircularDependencyRuleId,
            "Static class circular dependency — TypeInitializationException risk",
            "Static class '{0}' is part of a circular call dependency chain. " +
            "This causes TypeInitializationException at unpredictable times. " +
            "Break the cycle by extracting shared logic into a non-static helper. " +
            "Cycle: {1}",
            DiagnosticCategories.UnityBestPractices,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
