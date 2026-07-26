namespace CycloneGames.InputSystem.Runtime
{
    public readonly struct InputPlayerMemoryStats
    {
        public InputPlayerMemoryStats(
            int actionCount,
            int contextDefinitionCount,
            int activeContextCount,
            int subjectCount,
            int pollingActionCount,
            int holdStateCount,
            int contextStackCount = 0,
            int captureStackCount = 0)
        {
            ActionCount = actionCount;
            ContextDefinitionCount = contextDefinitionCount;
            ActiveContextCount = activeContextCount;
            SubjectCount = subjectCount;
            PollingActionCount = pollingActionCount;
            HoldStateCount = holdStateCount;
            ContextStackCount = contextStackCount;
            CaptureStackCount = captureStackCount;
        }

        public int ActionCount { get; }
        public int ContextDefinitionCount { get; }
        public int ActiveContextCount { get; }
        public int SubjectCount { get; }
        public int PollingActionCount { get; }
        public int HoldStateCount { get; }
        public int ContextStackCount { get; }
        public int CaptureStackCount { get; }
    }

    public readonly struct InputManagerMemoryStats
    {
        public InputManagerMemoryStats(
            int activePlayerCount,
            int maximumPlayerCount,
            int joinInProgressCount,
            int reservedDeviceCount,
            int bindingOverrideProfileCount,
            int actionCount,
            int contextDefinitionCount,
            int activeContextCount,
            int subjectCount,
            int pollingActionCount,
            int holdStateCount,
            int contextStackCount = 0,
            int captureStackCount = 0,
            int maximumCaptureStackCount = 0)
        {
            ActivePlayerCount = activePlayerCount;
            MaximumPlayerCount = maximumPlayerCount;
            JoinInProgressCount = joinInProgressCount;
            ReservedDeviceCount = reservedDeviceCount;
            BindingOverrideProfileCount = bindingOverrideProfileCount;
            ActionCount = actionCount;
            ContextDefinitionCount = contextDefinitionCount;
            ActiveContextCount = activeContextCount;
            SubjectCount = subjectCount;
            PollingActionCount = pollingActionCount;
            HoldStateCount = holdStateCount;
            ContextStackCount = contextStackCount;
            CaptureStackCount = captureStackCount;
            MaximumCaptureStackCount = maximumCaptureStackCount;
        }

        public int ActivePlayerCount { get; }
        public int MaximumPlayerCount { get; }
        public int JoinInProgressCount { get; }
        public int ReservedDeviceCount { get; }
        public int BindingOverrideProfileCount { get; }
        public int ActionCount { get; }
        public int ContextDefinitionCount { get; }
        public int ActiveContextCount { get; }
        public int SubjectCount { get; }
        public int PollingActionCount { get; }
        public int HoldStateCount { get; }
        public int ContextStackCount { get; }
        public int CaptureStackCount { get; }
        public int MaximumCaptureStackCount { get; }
    }
}
