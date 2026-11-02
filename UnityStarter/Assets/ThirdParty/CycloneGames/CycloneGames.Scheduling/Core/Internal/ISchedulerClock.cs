namespace CycloneGames.Scheduling.Core
{
    internal interface ISchedulerClock
    {
        long GetTimestamp();

        double ToMilliseconds(long ticks);

        /// <summary>
        /// Converts a duration to a timestamp delta, rounding up so a budget is never understated.
        /// </summary>
        long ToTicks(double milliseconds);
    }
}
