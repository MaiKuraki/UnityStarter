namespace CycloneGames.DataTable.Unity.Editor
{
    internal enum DataTableLubanRunnerPhase
    {
        Idle,
        Preparing,
        StartingProcess,
        Running,
        CancellationRequested,
        Completing,
        Succeeded,
        Failed,
        Cancelled,
        TimedOut,
        RecoveryRequired,
    }

    /// <summary>
    /// Immutable polling snapshot for the process-wide Editor pipeline runner.
    /// </summary>
    internal readonly struct DataTableLubanRunnerState
    {
        internal DataTableLubanRunnerState(
            long revision,
            DataTableLubanRunnerPhase phase,
            bool isActive,
            DataTableLubanOperation operation,
            string profileName,
            string buildConfigurationPath,
            int processId,
            long startedUtcTicks,
            long updatedUtcTicks,
            bool hasLastResult,
            DataTableLubanRunResult lastResult)
        {
            Revision = revision;
            Phase = phase;
            IsActive = isActive;
            Operation = operation;
            ProfileName = profileName ?? string.Empty;
            BuildConfigurationPath = buildConfigurationPath ?? string.Empty;
            ProcessId = processId;
            StartedUtcTicks = startedUtcTicks;
            UpdatedUtcTicks = updatedUtcTicks;
            HasLastResult = hasLastResult;
            LastResult = lastResult;
        }

        public long Revision { get; }
        public DataTableLubanRunnerPhase Phase { get; }
        public bool IsActive { get; }
        public bool CanCancel =>
            IsActive &&
            Phase != DataTableLubanRunnerPhase.CancellationRequested &&
            Phase != DataTableLubanRunnerPhase.Completing;
        public DataTableLubanOperation Operation { get; }
        public string ProfileName { get; }
        public string BuildConfigurationPath { get; }
        public int ProcessId { get; }
        public long StartedUtcTicks { get; }
        public long UpdatedUtcTicks { get; }
        public bool HasLastResult { get; }
        public DataTableLubanRunResult LastResult { get; }

        internal static DataTableLubanRunnerState CreateIdle(long revision)
        {
            long now = System.DateTime.UtcNow.Ticks;
            return new DataTableLubanRunnerState(
                revision,
                DataTableLubanRunnerPhase.Idle,
                false,
                default,
                string.Empty,
                string.Empty,
                0,
                0,
                now,
                false,
                default);
        }
    }
}
