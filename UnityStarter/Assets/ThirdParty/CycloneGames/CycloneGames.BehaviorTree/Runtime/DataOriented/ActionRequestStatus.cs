namespace CycloneGames.BehaviorTree.Runtime.DOD
{
    /// <summary>
    /// Lifecycle of one external action request owned by the DOD scheduler.
    /// The scheduler publishes Requested; the external owner may move it to Running,
    /// then complete it with Success or Failed. Reset and invalidation return it to Idle.
    /// </summary>
    public enum ActionRequestStatus : byte
    {
        Idle = 0,
        Requested = 1,
        Running = 2,
        Success = 3,
        Failed = 4
    }
}
