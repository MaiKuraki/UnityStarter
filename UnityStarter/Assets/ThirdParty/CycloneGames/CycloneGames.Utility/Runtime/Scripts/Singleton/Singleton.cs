namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Provides one lazily constructed instance for a parameterless managed type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CLR type-initialization semantics make construction and publication thread-safe. They do not make
    /// mutable state inside <typeparamref name="T"/> thread-safe.
    /// </para>
    /// <para>
    /// The instance lives for the current application domain and has no reset or shutdown contract. Use this
    /// only for resource-free, process-lifetime objects. Services that own subscriptions, threads, native
    /// resources, cancellation, or disposal must have an explicit owner instead.
    /// </para>
    /// <para>
    /// After initialization, access is a static field read and does not allocate managed memory.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">A reference type with a public parameterless constructor.</typeparam>
    public abstract class Singleton<T> where T : class, new()
    {
        private static class Holder
        {
            internal static readonly T Value = new T();

            // Prevent beforefieldinit so construction remains strictly lazy.
            static Holder()
            {
            }
        }

        /// <summary>Gets the application-domain instance.</summary>
        public static T Instance => Holder.Value;
    }
}
