namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Backend-agnostic contract for realizing VFX clips. The scheduler pushes resolved samples; the concrete
    /// provider maps them to a particle system, VFX graph, or pooled effect instance.
    /// </summary>
    public interface IVfxProvider
    {
        void BeginClip(in ChoreographyPlaybackSample sample);

        void UpdateClip(in ChoreographyPlaybackSample sample);

        void EndClip(in ChoreographyClipStop stop);
    }
}
