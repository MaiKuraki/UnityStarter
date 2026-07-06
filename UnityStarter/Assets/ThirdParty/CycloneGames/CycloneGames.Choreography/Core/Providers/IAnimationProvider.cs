namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Backend-agnostic contract for realizing animation clips. The scheduler pushes resolved samples;
    /// the concrete provider (Animancer, Spine, sprite sequence, ...) maps them to its own runtime.
    /// Implementations must tolerate a resource that failed to preload (log a warning, no hard crash).
    /// </summary>
    public interface IAnimationProvider
    {
        /// <summary>Called on the first tick a clip becomes active. The provider starts playback for the sample.</summary>
        void BeginClip(in ChoreographyPlaybackSample sample);

        /// <summary>Called every subsequent tick while a clip stays active. Carries the resolved weight and local time.</summary>
        void UpdateClip(in ChoreographyPlaybackSample sample);

        /// <summary>Called when a clip stops (completed or interrupted). The provider tears down its state for the clip.</summary>
        void EndClip(in ChoreographyClipStop stop);
    }
}
