namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Connection-scoped rate limiter abstraction.
    /// </summary>
    public interface IConnectionRateLimiter
    {
        bool TryConsume(int connectionId, int tokens);
    }
}