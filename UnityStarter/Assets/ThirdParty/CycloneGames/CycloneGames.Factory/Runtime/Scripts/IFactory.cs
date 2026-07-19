namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Marks a type as an explicit object-creation boundary.
    /// </summary>
    public interface IFactory
    {
    }

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

}
