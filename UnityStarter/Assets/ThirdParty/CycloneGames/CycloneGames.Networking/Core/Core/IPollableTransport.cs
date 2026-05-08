namespace CycloneGames.Networking
{
    /// <summary>
    /// Optional extension for <see cref="INetTransport"/> implementations that require
    /// explicit polling to process inbound messages and fire connection events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most real-network transports (KCP, ENet, TCP) drive event processing internally
    /// via socket I/O threads or OS callbacks. In-process transports such as the local
    /// loopback transport have no I/O thread and must be polled from the game loop.
    /// </para>
    /// <para>
    /// Callers should check <c>transport is IPollableTransport</c> and call
    /// <see cref="PollEvents"/> once per frame from the main thread.
    /// </para>
    /// <para>
    /// Thread Safety: Must be called from a single thread. Not safe for concurrent
    /// invocation from multiple threads.
    /// </para>
    /// </remarks>
    public interface IPollableTransport : INetTransport
    {
        /// <summary>
        /// Drain pending inbound messages and fire any queued connection events.
        /// Call once per frame from the main thread.
        /// </summary>
        void PollEvents();
    }
}
