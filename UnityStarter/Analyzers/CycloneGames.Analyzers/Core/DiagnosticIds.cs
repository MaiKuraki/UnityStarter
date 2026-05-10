namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Diagnostic ID convention: CGxxxx where xxxx is a 4-digit number.
    /// Range allocation:
    ///   CG0001-CG0009: Performance (hot path violations)
    ///   CG0010-CG0019: Safety (forbidden APIs, memory leaks)
    ///   CG0020-CG0029: ApiContract (framework-specific API rules)
    ///   CG0030-CG0039: Convention (code style, naming, structure)
    /// </summary>
    public static class DiagnosticIds
    {
        // ── Performance (CG0001-CG0009) ──
        public const string HotPathForEach         = "CG0001";
        public const string HotPathLinq            = "CG0002";
        public const string HotPathStringConcat    = "CG0003";
        public const string CameraMainInHotPath    = "CG0004";

        // ── Safety (CG0010-CG0019) ──
        public const string GameObjectFind         = "CG0010";
        public const string FindObjectOfType       = "CG0011";
        public const string SendMessageApi         = "CG0012";
        public const string InvokeApi              = "CG0013";
        public const string ResourcesLoad          = "CG0014";
        public const string NativeContainerLeak    = "CG0015";

        // ── ApiContract (CG0020-CG0029) ──
        public const string NetworkVariableBlittability = "CG0020";
        public const string RpcMethodSignature     = "CG0021";
        public const string ActorStartBaseCall     = "CG0022";
        public const string PoolOnDespawnOverride  = "CG0023";
        public const string GameplayTagImplicitCast= "CG0024";

        // ── Convention (CG0030-CG0039) ──
        public const string PublicFieldOnMonoBehaviour = "CG0030";
        public const string UsingStaticDirective   = "CG0031";
        public const string RegionInRuntime        = "CG0032";
        public const string ObsoleteInFramework    = "CG0033";

        // ── Unity Best Practices (CG0040-CG0049) ──
        public const string AsyncVoidInRuntime     = "CG0040";
        public const string TransformChainAccess   = "CG0041";
        public const string UnityEditorInRuntime   = "CG0042";
        public const string DebugLogInHotPath      = "CG0043";
        public const string GetComponentInHotPath  = "CG0044";
        public const string BoxingInHotPath        = "CG0045";
        public const string LambdaAllocInHotPath   = "CG0046";
        public const string AsyncTaskOverUniTask   = "CG0047";
        public const string StaticClassCircularDependencyRuleId = "CG0048";
    }
}
