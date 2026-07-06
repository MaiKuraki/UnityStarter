namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Backend-agnostic contract for realizing audio clips. The scheduler pushes resolved samples; the
    /// concrete provider (Unity AudioSource, Wwise, CriWare, ...) maps them to its own runtime and honors the
    /// resolved <see cref="ChoreographyPlaybackSample.Weight"/> as a volume/mix factor.
    /// </summary>
    public interface IAudioProvider
    {
        void BeginClip(in ChoreographyPlaybackSample sample);

        void UpdateClip(in ChoreographyPlaybackSample sample);

        void EndClip(in ChoreographyClipStop stop);
    }
}
