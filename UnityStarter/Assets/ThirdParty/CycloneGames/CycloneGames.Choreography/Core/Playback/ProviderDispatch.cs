namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Routes resolved samples and stops to the matching provider in an <see cref="IChoreographyProviderSet"/>.
    /// Missing providers produce a throttled warning instead of a null-reference crash, so a host that only
    /// supports a subset of content kinds degrades gracefully.
    /// </summary>
    internal static class ProviderDispatch
    {
        internal struct ThrottleState
        {
            private int _missingProviderMask;

            public bool TryMarkMissingProvider(ChoreographyTrackKind kind)
            {
                int index = (int)kind;
                if (index < 0 || index >= 31)
                {
                    return true;
                }

                int mask = 1 << index;
                if ((_missingProviderMask & mask) != 0)
                {
                    return false;
                }

                _missingProviderMask |= mask;
                return true;
            }
        }

        internal static void Begin(
            IChoreographyProviderSet providers,
            IChoreographyDiagnostics diagnostics,
            ref ThrottleState throttle,
            in ChoreographyPlaybackSample sample)
        {
            switch (sample.TrackKind)
            {
                case ChoreographyTrackKind.Animation:
                    if (providers.Animation != null) { providers.Animation.BeginClip(in sample); }
                    else { WarnMissing(diagnostics, ref throttle, sample.TrackKind, sample.Clip.Id); }
                    break;
                case ChoreographyTrackKind.Audio:
                    if (providers.Audio != null) { providers.Audio.BeginClip(in sample); }
                    else { WarnMissing(diagnostics, ref throttle, sample.TrackKind, sample.Clip.Id); }
                    break;
                case ChoreographyTrackKind.Vfx:
                    if (providers.Vfx != null) { providers.Vfx.BeginClip(in sample); }
                    else { WarnMissing(diagnostics, ref throttle, sample.TrackKind, sample.Clip.Id); }
                    break;
            }
        }

        internal static void Update(IChoreographyProviderSet providers, in ChoreographyPlaybackSample sample)
        {
            switch (sample.TrackKind)
            {
                case ChoreographyTrackKind.Animation:
                    if (providers.Animation != null) { providers.Animation.UpdateClip(in sample); }
                    break;
                case ChoreographyTrackKind.Audio:
                    if (providers.Audio != null) { providers.Audio.UpdateClip(in sample); }
                    break;
                case ChoreographyTrackKind.Vfx:
                    if (providers.Vfx != null) { providers.Vfx.UpdateClip(in sample); }
                    break;
            }
        }

        internal static void End(IChoreographyProviderSet providers, in ChoreographyClipStop stop)
        {
            switch (stop.TrackKind)
            {
                case ChoreographyTrackKind.Animation:
                    if (providers.Animation != null) { providers.Animation.EndClip(in stop); }
                    break;
                case ChoreographyTrackKind.Audio:
                    if (providers.Audio != null) { providers.Audio.EndClip(in stop); }
                    break;
                case ChoreographyTrackKind.Vfx:
                    if (providers.Vfx != null) { providers.Vfx.EndClip(in stop); }
                    break;
            }
        }

        private static void WarnMissing(
            IChoreographyDiagnostics diagnostics,
            ref ThrottleState throttle,
            ChoreographyTrackKind kind,
            string clipId)
        {
            if (throttle.TryMarkMissingProvider(kind) && diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
            {
                diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography",
                    "No provider registered for track kind '" + kind + "'; skipping clip '" + clipId + "'. Further warnings for this track kind are suppressed.");
            }
        }
    }
}
