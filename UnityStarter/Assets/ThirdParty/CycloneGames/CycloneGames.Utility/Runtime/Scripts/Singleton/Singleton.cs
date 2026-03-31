namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// High-performance, thread-safe, lazy singleton base class.
    /// Uses the nested Holder pattern to guarantee:
    ///   1. Strict lazy initialization (instance created only on first Instance access).
    ///   2. Thread safety (guaranteed by CLR type initializer semantics).
    ///   3. Zero per-access GC allocation (single static field read).
    /// </summary>
    /// <example>
    /// // --- Recommended: use directly without inheritance ---
    /// public class AudioService { public void PlaySFX(string id) { ... } }
    /// // Usage: Singleton<AudioService>.Instance.PlaySFX("click");
    ///
    /// // --- Alternative: inherit for shorter syntax ---
    /// public class GameManager : Singleton<GameManager>; { public void StartGame() { ... } }
    /// // Usage: GameManager.Instance.StartGame();
    ///
    /// // Both share the same thread-safe, lazy, zero-GC guarantee.
    /// </example>
    public abstract class Singleton<T> where T : class, new()
    {
        // Nested class ensures the instance is created only when Holder is first accessed,
        // which only happens via the Instance property. The explicit static constructor
        // prevents the CLR's beforefieldinit optimization from triggering early initialization.
        private static class Holder
        {
            internal static readonly T Value = new T();
            static Holder() { }
        }

        public static T Instance => Holder.Value;
    }

    /*
    // Recommended: use directly, no inheritance needed
    public class AudioService { public void PlaySFX(string id) { ... } }
    Singleton<AudioService>.Instance.PlaySFX("click");

    // Alternative: inherit for shorter access syntax
    public class GameManager : Singleton<GameManager> { public void StartGame() { ... } }
    GameManager.Instance.StartGame();

    // Both are thread-safe, lazy-initialized, zero-GC.
    */
}