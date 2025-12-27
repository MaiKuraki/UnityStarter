namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Generic state pool using flyweight pattern.
    /// States are stateless singletons shared across all MovementComponent instances.
    /// </summary>
    public static class StatePool<TState> where TState : class
    {
        public static T GetState<T>() where T : TState, new()
        {
            return StateInstanceCache<T>.Instance ??= new T();
        }

        private static class StateInstanceCache<T> where T : TState, new()
        {
            public static T Instance;
        }
    }
}