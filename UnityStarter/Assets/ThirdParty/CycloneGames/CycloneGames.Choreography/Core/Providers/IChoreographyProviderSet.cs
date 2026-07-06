namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Bundles the providers a <see cref="ChoreographyScheduler"/> dispatches to. Any member may be null when a
    /// host does not support that content kind; the scheduler logs a warning and skips the corresponding track
    /// rather than throwing. Kept as an interface so hosts can back it with DI, a registry, or a plain container.
    /// </summary>
    public interface IChoreographyProviderSet
    {
        IAnimationProvider Animation { get; }

        IAudioProvider Audio { get; }

        IVfxProvider Vfx { get; }

        /// <summary>Resource provider used for preload and load-on-demand. May be null when resources are host-managed.</summary>
        IResourceProvider Resources { get; }
    }
}
