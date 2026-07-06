using System;
using System.Collections.Generic;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Coordinates multiple concurrent <see cref="ChoreographyPlayer"/> instances and resolves how they compete
    /// for shared channels using pluggable <see cref="IPlaybackStrategy"/> rules. The scheduler is engine-free and
    /// advanced by explicit <see cref="Tick"/> calls; the Unity layer drives it from a MonoBehaviour update loop.
    ///
    /// Per tick the scheduler advances every active player (each player reports lifecycle callbacks into this
    /// scheduler as its sink), buffers the active samples, groups them by channel and track kind, applies the
    /// governing strategy's weight to each sample, and finally dispatches begin/update calls to the providers.
    /// Clip stops and events are dispatched immediately as they occur. Players and instance records are pooled so
    /// steady-state playback allocates only when the per-tick sample buffer must grow.
    /// </summary>
    public sealed class ChoreographyScheduler : IChoreographyPlaybackSink
    {
        public const int InvalidInstanceId = 0;

        private sealed class Instance
        {
            public int Id;
            public ChoreographyPlayer Player;
            public int Channel;
            public int Priority;
            public ChoreographyPlaybackMode Mode;
            public double Speed;
            public bool Loop;
            public bool PendingRemove;
        }

        private struct QueuedRequest
        {
            public int Id;
            public IChoreographyAsset Asset;
            public int Channel;
            public int Priority;
            public ChoreographyPlaybackMode Mode;
            public double Speed;
            public bool Loop;
        }

        private struct BufferedSample
        {
            public ChoreographyPlaybackSample Sample;
            public int Priority;
            public ChoreographyPlaybackMode Mode;
            public bool IsStart;
        }

        private sealed class BufferedSampleComparer : IComparer<BufferedSample>
        {
            public int Compare(BufferedSample x, BufferedSample y)
            {
                int channelCompare = x.Sample.Channel.CompareTo(y.Sample.Channel);
                if (channelCompare != 0)
                {
                    return channelCompare;
                }
                return ((byte)x.Sample.TrackKind).CompareTo((byte)y.Sample.TrackKind);
            }
        }

        private static readonly BufferedSampleComparer SampleComparer = new BufferedSampleComparer();

        private readonly IChoreographyProviderSet _providers;
        private readonly IChoreographyDiagnostics _diagnostics;
        private readonly IPlaybackStrategy[] _strategies = new IPlaybackStrategy[5];

        private readonly List<Instance> _instances = new List<Instance>(16);
        private readonly List<QueuedRequest> _queue = new List<QueuedRequest>(8);
        private readonly Stack<Instance> _instancePool = new Stack<Instance>(16);
        private readonly Stack<ChoreographyPlayer> _playerPool = new Stack<ChoreographyPlayer>(16);

        private BufferedSample[] _sampleBuffer = new BufferedSample[64];
        private int _sampleCount;
        private Instance _currentTickInstance;
        private ProviderDispatch.ThrottleState _dispatchThrottle;
        private int _nextId = 1;

        /// <summary>Raised for every timeline event crossed by any instance during <see cref="Tick"/>.</summary>
        public event Action<ChoreographyEventInvocation> EventRaised;

        /// <summary>Raised when an instance completes (reaches the end without looping) or is stopped.</summary>
        public event Action<int> InstanceEnded;

        public ChoreographyScheduler(IChoreographyProviderSet providers, IChoreographyDiagnostics diagnostics = null)
        {
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _diagnostics = diagnostics ?? NullChoreographyDiagnostics.Instance;

            RegisterStrategy(PriorityPlaybackStrategy.Instance);
            RegisterStrategy(BlendPlaybackStrategy.Instance);
            RegisterStrategy(OverridePlaybackStrategy.Instance);
            RegisterStrategy(AdditivePlaybackStrategy.Instance);
            RegisterStrategy(QueuePlaybackStrategy.Instance);
        }

        public int ActiveCount => _instances.Count;

        public int QueuedCount => _queue.Count;

        /// <summary>Registers (or replaces) the strategy handling its <see cref="IPlaybackStrategy.Mode"/>.</summary>
        public void RegisterStrategy(IPlaybackStrategy strategy)
        {
            if (strategy == null)
            {
                throw new ArgumentNullException(nameof(strategy));
            }

            int index = (int)strategy.Mode;
            if (index < 0 || index >= _strategies.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(strategy), "Strategy mode is outside the scheduler strategy table.");
            }

            _strategies[index] = strategy;
        }

        /// <summary>
        /// Requests playback of <paramref name="asset"/> on the requested channel. Returns a positive instance id
        /// when the request is admitted or queued, or <see cref="InvalidInstanceId"/> when rejected by the strategy.
        /// </summary>
        public int Play(IChoreographyAsset asset, in ChoreographyPlayRequest request)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            ComputeChannelAggregates(
                request.Channel,
                out int activeCount,
                out int highestPriority,
                out bool dominantInterruptible,
                out ChoreographyPlaybackMode dominantMode);

            ChoreographyPlaybackMode admissionMode = activeCount > 0 ? dominantMode : request.Mode;
            IPlaybackStrategy strategy = ResolveStrategy(admissionMode);

            ChoreographyAdmission admission = strategy.Resolve(new ChoreographyStrategyContext(
                request.Channel, request.Priority, activeCount, highestPriority, dominantInterruptible));

            int id = _nextId++;
            switch (admission)
            {
                case ChoreographyAdmission.Admit:
                    StartInstance(id, asset, request.Channel, request.Priority, request.Mode, request.Speed, request.Loop);
                    return id;

                case ChoreographyAdmission.Replace:
                    StopChannelInternal(request.Channel, false);
                    StartInstance(id, asset, request.Channel, request.Priority, request.Mode, request.Speed, request.Loop);
                    return id;

                case ChoreographyAdmission.Queue:
                    _queue.Add(new QueuedRequest
                    {
                        Id = id,
                        Asset = asset,
                        Channel = request.Channel,
                        Priority = request.Priority,
                        Mode = request.Mode,
                        Speed = request.Speed,
                        Loop = request.Loop
                    });
                    return id;

                default:
                    if (_diagnostics.IsEnabled(ChoreographyLogLevel.Info))
                    {
                        _diagnostics.Log(ChoreographyLogLevel.Info, "Choreography",
                            "Play request rejected on channel " + request.Channel + " for asset '" + asset.Id + "'.");
                    }
                    return InvalidInstanceId;
            }
        }

        /// <summary>Stops a specific instance (interrupting its active clips). Also removes it from the queue if pending.</summary>
        public void Stop(int instanceId)
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                if (_instances[i].Id == instanceId)
                {
                    _currentTickInstance = _instances[i];
                    _instances[i].Player.Stop();
                    _currentTickInstance = null;
                    _instances[i].PendingRemove = true;
                    return;
                }
            }

            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (_queue[i].Id == instanceId)
                {
                    _queue.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>Stops every active instance on a channel and clears its queued requests.</summary>
        public void StopChannel(int channel)
        {
            StopChannelInternal(channel, false);
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (_queue[i].Channel == channel)
                {
                    _queue.RemoveAt(i);
                }
            }
        }

        /// <summary>Stops all instances and clears the queue.</summary>
        public void StopAll()
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                _currentTickInstance = _instances[i];
                _instances[i].Player.Stop();
                _instances[i].PendingRemove = true;
            }
            _currentTickInstance = null;
            _queue.Clear();
            RemovePending();
        }

        /// <summary>
        /// Advances all active players by <paramref name="deltaTime"/> seconds, resolves per-channel weights, and
        /// dispatches to providers. Completed instances are recycled and queued requests are promoted afterwards.
        /// </summary>
        public void Tick(double deltaTime)
        {
            Tick(ChoreographyTimelineStep.FromDelta(deltaTime));
        }

        /// <summary>
        /// Advances all active players from an explicit time authority sample, resolves per-channel weights, and
        /// dispatches to providers. Completed instances are recycled and queued requests are promoted afterwards.
        /// </summary>
        public void Tick(in ChoreographyTimelineStep step)
        {
            _sampleCount = 0;

            for (int i = 0; i < _instances.Count; i++)
            {
                Instance instance = _instances[i];
                if (instance.PendingRemove)
                {
                    continue;
                }
                _currentTickInstance = instance;
                instance.Player.Tick(step);
            }
            _currentTickInstance = null;

            ResolveAndDispatch();
            RemovePending();
            PromoteQueue();
        }

        private void ResolveAndDispatch()
        {
            if (_sampleCount == 0)
            {
                return;
            }

            if (_sampleCount > 1)
            {
                Array.Sort(_sampleBuffer, 0, _sampleCount, SampleComparer);
            }

            int groupStart = 0;
            while (groupStart < _sampleCount)
            {
                int channel = _sampleBuffer[groupStart].Sample.PlaybackChannel;
                ChoreographyTrackKind kind = _sampleBuffer[groupStart].Sample.TrackKind;

                int groupEnd = groupStart + 1;
                float totalWeight = _sampleBuffer[groupStart].Sample.Weight;
                int highestPriority = _sampleBuffer[groupStart].Priority;
                ChoreographyPlaybackMode dominantMode = _sampleBuffer[groupStart].Mode;
                while (groupEnd < _sampleCount
                    && _sampleBuffer[groupEnd].Sample.PlaybackChannel == channel
                    && _sampleBuffer[groupEnd].Sample.TrackKind == kind)
                {
                    totalWeight += _sampleBuffer[groupEnd].Sample.Weight;
                    if (_sampleBuffer[groupEnd].Priority > highestPriority)
                    {
                        highestPriority = _sampleBuffer[groupEnd].Priority;
                        dominantMode = _sampleBuffer[groupEnd].Mode;
                    }
                    groupEnd++;
                }

                int activeCount = groupEnd - groupStart;
                IPlaybackStrategy strategy = ResolveStrategy(dominantMode);

                for (int i = groupStart; i < groupEnd; i++)
                {
                    BufferedSample buffered = _sampleBuffer[i];
                    float weight = strategy.ResolveWeight(new ChoreographyWeightContext(
                        channel, buffered.Priority, highestPriority, activeCount, buffered.Sample.Weight, totalWeight));

                    ChoreographyPlaybackSample resolved = new ChoreographyPlaybackSample(
                        buffered.Sample.InstanceId,
                        buffered.Sample.TrackKind,
                        buffered.Sample.Clip,
                        buffered.Sample.TimelineTime,
                        buffered.Sample.LocalTime,
                        buffered.Sample.NormalizedTime,
                        weight,
                        channel,
                        buffered.Sample.ClipChannel,
                        buffered.Sample.ClockKind,
                        buffered.Sample.TickIndex,
                        buffered.Sample.SourceTime);

                    if (buffered.IsStart)
                    {
                        ProviderDispatch.Begin(_providers, _diagnostics, ref _dispatchThrottle, in resolved);
                    }
                    else
                    {
                        ProviderDispatch.Update(_providers, in resolved);
                    }
                }

                groupStart = groupEnd;
            }
        }

        private void StartInstance(int id, IChoreographyAsset asset, int channel, int priority, ChoreographyPlaybackMode mode, double speed, bool loop)
        {
            Instance instance = _instancePool.Count > 0 ? _instancePool.Pop() : new Instance();
            ChoreographyPlayer player = instance.Player ?? (_playerPool.Count > 0 ? _playerPool.Pop() : new ChoreographyPlayer());
            player.SetDiagnostics(_diagnostics);

            instance.Id = id;
            instance.Player = player;
            instance.Channel = channel;
            instance.Priority = priority;
            instance.Mode = mode;
            instance.Speed = speed;
            instance.Loop = loop;
            instance.PendingRemove = false;

            player.Load(asset, new ChoreographyPlaybackContext(id, channel, speed, loop, mode), this);
            player.Play();
            _instances.Add(instance);
        }

        private void StopChannelInternal(int channel, bool completed)
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                Instance instance = _instances[i];
                if (instance.Channel == channel && !instance.PendingRemove)
                {
                    _currentTickInstance = instance;
                    instance.Player.Stop();
                    instance.PendingRemove = true;
                }
            }
            _currentTickInstance = null;
        }

        private void RemovePending()
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                Instance instance = _instances[i];
                if (!instance.PendingRemove)
                {
                    continue;
                }

                int id = instance.Id;
                _instances.RemoveAt(i);
                _playerPool.Push(instance.Player);
                instance.Player = null;
                _instancePool.Push(instance);
                InstanceEnded?.Invoke(id);
            }
        }

        private void PromoteQueue()
        {
            for (int i = 0; i < _queue.Count; i++)
            {
                QueuedRequest queued = _queue[i];
                ComputeChannelAggregates(
                    queued.Channel,
                    out int activeCount,
                    out int highestPriority,
                    out bool dominantInterruptible,
                    out ChoreographyPlaybackMode dominantMode);

                ChoreographyAdmission admission = ChoreographyAdmission.Admit;
                if (activeCount > 0)
                {
                    IPlaybackStrategy strategy = ResolveStrategy(dominantMode);
                    admission = strategy.Resolve(new ChoreographyStrategyContext(
                        queued.Channel, queued.Priority, activeCount, highestPriority, dominantInterruptible));
                }

                if (admission == ChoreographyAdmission.Queue || admission == ChoreographyAdmission.Reject)
                {
                    continue;
                }

                _queue.RemoveAt(i);
                i--;

                if (admission == ChoreographyAdmission.Replace)
                {
                    StopChannelInternal(queued.Channel, false);
                    RemovePending();
                }

                StartInstance(queued.Id, queued.Asset, queued.Channel, queued.Priority, queued.Mode, queued.Speed, queued.Loop);
            }
        }

        private void ComputeChannelAggregates(
            int channel,
            out int activeCount,
            out int highestPriority,
            out bool dominantInterruptible,
            out ChoreographyPlaybackMode dominantMode)
        {
            activeCount = 0;
            highestPriority = int.MinValue;
            dominantInterruptible = true;
            dominantMode = ChoreographyPlaybackMode.Priority;

            for (int i = 0; i < _instances.Count; i++)
            {
                Instance instance = _instances[i];
                if (instance.Channel != channel || instance.PendingRemove)
                {
                    continue;
                }

                activeCount++;
                if (instance.Priority > highestPriority)
                {
                    highestPriority = instance.Priority;
                    dominantInterruptible = instance.Player.CurrentSectionInterruptible;
                    dominantMode = instance.Player.CurrentSectionMode;
                }
            }

            if (activeCount == 0)
            {
                highestPriority = 0;
            }
        }

        private IPlaybackStrategy ResolveStrategy(ChoreographyPlaybackMode mode)
        {
            int index = (int)mode;
            if (index >= 0 && index < _strategies.Length && _strategies[index] != null)
            {
                return _strategies[index];
            }

            if (_diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
            {
                _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography",
                    "Unknown playback mode '" + mode + "'; falling back to Priority.");
            }

            return PriorityPlaybackStrategy.Instance;
        }

        private void EnsureSampleCapacity()
        {
            if (_sampleCount >= _sampleBuffer.Length)
            {
                Array.Resize(ref _sampleBuffer, _sampleBuffer.Length << 1);
            }
        }

        // --- IChoreographyPlaybackSink (explicitly implemented; players call these while ticking) ---

        void IChoreographyPlaybackSink.OnClipStarted(in ChoreographyPlaybackSample sample) => BufferSample(in sample, true);

        void IChoreographyPlaybackSink.OnClipUpdated(in ChoreographyPlaybackSample sample) => BufferSample(in sample, false);

        void IChoreographyPlaybackSink.OnClipStopped(in ChoreographyClipStop stop) => ProviderDispatch.End(_providers, in stop);

        void IChoreographyPlaybackSink.OnEvent(in ChoreographyEventInvocation invocation) => EventRaised?.Invoke(invocation);

        void IChoreographyPlaybackSink.OnPlaybackCompleted(int instanceId)
        {
            if (_currentTickInstance != null && _currentTickInstance.Id == instanceId)
            {
                _currentTickInstance.PendingRemove = true;
            }
        }

        private void BufferSample(in ChoreographyPlaybackSample sample, bool isStart)
        {
            EnsureSampleCapacity();
            _sampleBuffer[_sampleCount].Sample = sample;
            _sampleBuffer[_sampleCount].Priority = _currentTickInstance != null ? _currentTickInstance.Priority : 0;
            _sampleBuffer[_sampleCount].Mode = _currentTickInstance != null
                ? _currentTickInstance.Player.CurrentSectionMode
                : ChoreographyPlaybackMode.Priority;
            _sampleBuffer[_sampleCount].IsStart = isStart;
            _sampleCount++;
        }
    }
}
