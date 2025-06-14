namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// A marker interface for all factory types.
    /// </summary>
    public interface IFactory { }

    /// <summary>
    /// Defines a factory that creates objects of a specific type.
    /// </summary>
    /// <typeparam name="TValue">The type of object to create.</typeparam>
    public interface IFactory<out TValue> : IFactory
    {
        /// <summary>
        /// Creates a new instance of <typeparamref name="TValue"/>.
        /// </summary>
        /// <returns>A new object.</returns>
        TValue Create();
    }

    /// <summary>
    /// Defines a factory that creates objects of a specific type using an argument.
    /// </summary>
    /// <typeparam name="TArg">The type of argument used for creation.</typeparam>
    /// <typeparam name="TValue">The type of object to create.</typeparam>
    public interface IFactory<in TArg, out TValue> : IFactory
    {
        /// <summary>
        /// Creates a new instance of <typeparamref name="TValue"/> using the provided argument.
        /// </summary>
        /// <param name="arg">The argument for creation.</param>
        /// <returns>A new object.</returns>
        TValue Create(TArg arg);
    }

    /// <summary>
    /// Defines a factory specialized in spawning <see cref="UnityEngine.Object"/> instances.
    /// </summary>
    public interface IUnityObjectSpawner : IFactory
    {
        /// <summary>
        /// Creates a new instance of a <see cref="UnityEngine.Object"/>.
        /// </summary>
        /// <param name="origin">The original object to clone or instantiate.</param>
        /// <typeparam name="T">The type of the object, constrained to <see cref="UnityEngine.Object"/>.</typeparam>
        /// <returns>A new instance of the object.</returns>
        T Create<T>(T origin) where T : UnityEngine.Object;
    }
}