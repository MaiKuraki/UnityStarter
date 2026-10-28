namespace CycloneGames.Scheduling.Core
{
    /// <summary>
    /// A finite unit of cooperative work. Implementations own their progress and partial results.
    /// A running Step cannot be preempted by the scheduler.
    /// </summary>
    public interface IWorkItem
    {
        WorkStepResult Step();

        /// <summary>
        /// Called exactly once after an accepted registration reaches a terminal state.
        /// The scheduler has released its internal reference before this callback runs.
        /// </summary>
        void OnTerminated(WorkTerminationReason reason);
    }
}
