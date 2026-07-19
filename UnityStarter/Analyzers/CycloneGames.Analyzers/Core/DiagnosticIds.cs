namespace CycloneGames.Analyzers
{
    /// <summary>
    /// Diagnostic ID convention: CGxxxx where xxxx is a 4-digit number.
    /// </summary>
    public static class DiagnosticIds
    {
        public const string HotPathForEach = "CG0001";
        public const string HotPathLinq = "CG0002";
        public const string HotPathStringConcat = "CG0003";
        public const string CameraMainInHotPath = "CG0004";

        public const string GameObjectFind = "CG0010";
        public const string FindObjectOfType = "CG0011";
        public const string SendMessageApi = "CG0012";
        public const string InvokeApi = "CG0013";
        public const string ResourcesLoad = "CG0014";
        public const string NativeContainerLeak = "CG0015";

        public const string ActorStartBaseCall = "CG0022";
        public const string PoolOnDespawnOverride = "CG0023";
        public const string GameplayTagImplicitCast = "CG0024";

        public const string PublicFieldOnMonoBehaviour = "CG0030";
        public const string UsingStaticDirective = "CG0031";
        public const string RegionInRuntime = "CG0032";
        public const string ObsoleteInFramework = "CG0033";

        public const string AsyncVoidInRuntime = "CG0040";
        public const string TransformChainAccess = "CG0041";
        public const string UnityEditorInRuntime = "CG0042";
        public const string DebugLogInHotPath = "CG0043";
        public const string GetComponentInHotPath = "CG0044";
        public const string BoxingInHotPath = "CG0045";
        public const string LambdaAllocInHotPath = "CG0046";
        public const string AsyncTaskOverUniTask = "CG0047";
        public const string StaticClassCircularDependencyRuleId = "CG0048";
    }
}
