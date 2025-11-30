namespace CycloneGames.Utility.Runtime
{
    public abstract class Singleton<T> where T : class, new()
    {
        // Static readonly ensures thread safety and lazy initialization (before field access).
        // This is the standard high-performance pattern in C#.
        private static readonly T instance = new T();

        /// <summary>
        /// Gets the single instance of the class.
        /// </summary>
        public static T Instance => instance;
    }

    /*
    // Example: Simple Singleton
    public class GameManager : Singleton<GameManager>
    {
        public void StartGame() { ... }
    }
    // Usage: GameManager.Instance.StartGame();
    */
}