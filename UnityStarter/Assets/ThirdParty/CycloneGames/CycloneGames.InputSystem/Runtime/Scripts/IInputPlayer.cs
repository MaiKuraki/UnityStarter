namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Full contract of a joined player. Code that only reads actions, only manages contexts or only rebinds should
    /// depend on <see cref="IInputActionReader"/>, <see cref="IInputContextController"/> or
    /// <see cref="IInputRebindOperations"/> instead, so a change in one area does not reach unrelated call sites.
    /// </summary>
    public interface IInputPlayer : IInputActionReader, IInputContextController, IInputRebindOperations
    {
        /// <summary>Zero-based slot this player occupies.</summary>
        int PlayerId { get; }
    }
}
