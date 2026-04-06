namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    /// <summary>
    /// Connection-scoped rate limiter abstraction.
    /// </summary>
    public interface IConnectionRateLimiter
    {
        bool TryConsume(int connectionId, int tokens);
    }
}