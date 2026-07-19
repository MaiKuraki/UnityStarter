namespace CycloneGames.GameplayAbilities.Runtime
{
    internal static class AbilityTaskTerminalCallbackGuard
    {
        public static bool TryBegin(
            AbilityTask task,
            ref bool terminalCallbackStarted,
            out ulong leaseGeneration)
        {
            leaseGeneration = 0UL;
            if (task == null || terminalCallbackStarted || (!task.IsActive && task.Ability == null))
            {
                return false;
            }

            terminalCallbackStarted = true;
            leaseGeneration = task.LeaseGeneration;
            return true;
        }
    }
}
