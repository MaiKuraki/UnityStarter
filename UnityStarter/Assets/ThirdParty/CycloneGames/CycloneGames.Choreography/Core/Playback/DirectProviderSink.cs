using System;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Playback sink that forwards a single <see cref="ChoreographyPlayer"/>'s callbacks straight to a provider
    /// set using the authored clip weight, with no cross-instance strategy resolution. Use this to drive one
    /// standalone choreography (e.g. a UI flourish or a non-competing effect) without a
    /// <see cref="ChoreographyScheduler"/>. For competing playback on shared channels, use the scheduler instead.
    /// </summary>
    public sealed class DirectProviderSink : IChoreographyPlaybackSink
    {
        private readonly IChoreographyProviderSet _providers;
        private readonly IChoreographyDiagnostics _diagnostics;
        private ProviderDispatch.ThrottleState _dispatchThrottle;

        /// <summary>Raised when a timeline event is crossed.</summary>
        public event Action<ChoreographyEventInvocation> EventRaised;

        /// <summary>Raised when the driven player reaches the end of its timeline.</summary>
        public event Action<int> PlaybackCompleted;

        public DirectProviderSink(IChoreographyProviderSet providers, IChoreographyDiagnostics diagnostics = null)
        {
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _diagnostics = diagnostics ?? NullChoreographyDiagnostics.Instance;
        }

        public void OnClipStarted(in ChoreographyPlaybackSample sample)
        {
            ProviderDispatch.Begin(_providers, _diagnostics, ref _dispatchThrottle, in sample);
        }

        public void OnClipUpdated(in ChoreographyPlaybackSample sample)
        {
            ProviderDispatch.Update(_providers, in sample);
        }

        public void OnClipStopped(in ChoreographyClipStop stop)
        {
            ProviderDispatch.End(_providers, in stop);
        }

        public void OnEvent(in ChoreographyEventInvocation invocation)
        {
            EventRaised?.Invoke(invocation);
        }

        public void OnPlaybackCompleted(int instanceId)
        {
            PlaybackCompleted?.Invoke(instanceId);
        }
    }
}
