namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Poll-based, engine-agnostic handle to an in-flight or completed resource load. Mirrors the operation
    /// model used across CycloneGames asset systems (IsDone/Progress/Error) so a Unity bridge can wrap a
    /// UniTask-based handle without leaking engine or third-party async types into the Core.
    /// </summary>
    public interface IChoreographyResourceHandle
    {
        ChoreographyResourceReference Reference { get; }

        /// <summary>True once the load has finished, whether it succeeded or failed.</summary>
        bool IsDone { get; }

        /// <summary>True only when the resource is available for use.</summary>
        bool Succeeded { get; }

        /// <summary>Load progress in [0, 1].</summary>
        float Progress { get; }

        /// <summary>Human-readable error when <see cref="Succeeded"/> is false; otherwise null.</summary>
        string Error { get; }

        /// <summary>Releases the caller's interest in the resource. Safe to call once per successful acquire.</summary>
        void Release();
    }

    /// <summary>
    /// Abstraction over the host asset system. The Core defines only load/query/release; the concrete
    /// implementation (e.g. an AssetManagement bridge) owns the real backend and reference counting.
    /// Implementations must be safe to call from a single logical owner and should reuse handles for
    /// identical references while they remain loaded.
    /// </summary>
    public interface IResourceProvider
    {
        /// <summary>
        /// Begins (or joins) loading the referenced resource and returns a poll-based handle.
        /// Never returns null; a failed request completes with <see cref="IChoreographyResourceHandle.Succeeded"/> == false.
        /// </summary>
        IChoreographyResourceHandle Load(in ChoreographyResourceReference reference);

        /// <summary>Returns true and the current handle if the reference is already loaded or loading.</summary>
        bool TryGet(in ChoreographyResourceReference reference, out IChoreographyResourceHandle handle);

        /// <summary>Releases one interest in the reference held by this provider's callers.</summary>
        void Release(in ChoreographyResourceReference reference);
    }
}
