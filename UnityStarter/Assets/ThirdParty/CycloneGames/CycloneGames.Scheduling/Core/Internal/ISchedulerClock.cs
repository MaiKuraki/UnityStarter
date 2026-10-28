namespace CycloneGames.Scheduling.Core
{
    internal interface ISchedulerClock
    {
        long GetTimestamp();
        double ToMilliseconds(long ticks);
    }
}
