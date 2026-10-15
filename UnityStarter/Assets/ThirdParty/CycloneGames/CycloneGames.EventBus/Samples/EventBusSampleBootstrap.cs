using System;
using System.Threading;
using UnityEngine;
using CycloneGames.EventBus.Core;
using CycloneGames.EventBus.Runtime;

namespace CycloneGames.EventBus.Sample
{
    /// <summary>
    /// Playground for the EventBus package. Add this component to any GameObject in a scene and
    /// enter Play mode: it builds an EventBusContext, wires every container (direct bus,
    /// EventStream, MpscEventQueue fed by a background thread) and drives realistic traffic so
    /// the debugger window has live data. The Editor bridge registers <see cref="ActiveContext"/>
    /// with the debugger automatically.
    ///
    /// All knobs are SerializeFields so traffic shape can be tuned from the Inspector at runtime.
    /// Delete the Samples folder to remove every trace of the playground.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class EventBusSampleBootstrap : MonoBehaviour
    {
        [Header("Traffic")]
        [SerializeField, Tooltip("Direct publishes of SampleValueChangedEvent per frame.")]
        private int directPublishesPerFrame = 2;

        [SerializeField, Tooltip("SampleMetricTickEvent writes into the EventStream per frame.")]
        private int streamWritesPerFrame = 16;

        [SerializeField, Tooltip("SampleAssetLoadedEvent enqueues per second from a background thread.")]
        private int backgroundEventsPerSecond = 200;

        [SerializeField, Tooltip("SampleBurstEvent burst size, fired once per second.")]
        private int burstSize = 300;

        [Header("Fault Injection")]
        [SerializeField, Tooltip("One subscriber throws periodically to exercise the error policy.")]
        private bool enableFaultySubscriber = true;

        [SerializeField, Tooltip("Continuously subscribe/unsubscribe to exercise compaction.")]
        private bool enableChurn = true;

        [SerializeField, Tooltip("Spawn subscribers that destroy themselves WITHOUT unsubscribing, "
            + "so the debugger shows destroyed-target leak warnings.")]
        private bool enableLeakDemo = true;

        [SerializeField, Tooltip("Every few seconds one handler republishes its own event type, "
            + "driving the dispatch depth to the ceiling so the Dropped counter becomes visible.")]
        private bool enableReentrancyDemo = true;

        [SerializeField, Tooltip("Flood a deliberately small stream so the Containers tab shows "
            + "Rejected (full) climbing, and periodically Clear it so Dropped (confirmed loss) does too.")]
        private bool enableBackpressureDemo = true;

        [Header("Budgets")]
        [SerializeField] private int maxEventsPerTargetPerFrame = 512;
        [SerializeField] private int maxEventsPerFrame = 4096;

        /// <summary>The live context for the Editor bridge; null outside Play mode.</summary>
        public static EventBusContext ActiveContext { get; private set; }

        private EventBusContext _context;
        private EventBusPump _pump;
        private EventBus<SampleValueChangedEvent> _valueBus;
        private EventBus<SampleAssetLoadedEvent> _assetBus;
        private EventBus<SampleMetricTickEvent> _metricBus;
        private EventBus<SampleBurstEvent> _burstBus;
        private EventStream<SampleMetricTickEvent> _metricStream;
        private MpscEventQueue<SampleAssetLoadedEvent> _assetQueue;
        private EventStream<SampleBurstEvent> _pressureStream;
        private SubscriptionScope _churnScope;

        private Thread _producerThread;
        private volatile bool _producerStop;
        private int _assetIdCounter;
        private int _metricSequence;
        private int _burstCounter;
        private float _nextBurstTime;
        private float _nextFaultTime;
        private float _nextChurnTime;
        private float _nextLeakTime;
        private float _nextReentrantTime;
        private float _nextPressureTime;
        private float _nextPressureClearTime;
        private bool _reentrantDemoActive;

        private void Awake()
        {
            _context = new EventBusBuilder()
                .WithConfiguration(new EventBusConfiguration(
                    publishErrorPolicy: PublishErrorPolicy.ContinueOnError))
                .Build();

            _valueBus = _context.GetOrCreateBus<SampleValueChangedEvent>();
            _assetBus = _context.GetOrCreateBus<SampleAssetLoadedEvent>();
            _metricBus = _context.GetOrCreateBus<SampleMetricTickEvent>();
            _burstBus = _context.GetOrCreateBus<SampleBurstEvent>();

            _metricStream = new EventStream<SampleMetricTickEvent>(1024);
            _assetQueue = new MpscEventQueue<SampleAssetLoadedEvent>(2048);
            // Deliberately undersized and never flushed: exists only so the Containers tab shows
            // backpressure counters climbing.
            _pressureStream = new EventStream<SampleBurstEvent>(64);

            _pump = new EventBusPump();
            _pump.AddStream(_metricStream, _metricBus);
            _pump.AddQueue(_assetQueue, _assetBus);

            // Surface the container counters on the debugger's Containers tab. Containers are
            // caller-owned; the context only holds the diagnostics references.
            _context.RegisterContainer(_metricStream);
            _context.RegisterContainer(_assetQueue);
            _context.RegisterContainer(_pressureStream);
            _context.RegisterContainer(_pump);
            if (_context.Commands is IEventBusContainerDiagnostics commandDiagnostics)
            {
                _context.RegisterContainer(commandDiagnostics);
            }

            SubscribeHandlers();
            StartProducer();

            _nextBurstTime = Time.unscaledTime + 1f;
            _nextFaultTime = Time.unscaledTime + 2f;
            _nextChurnTime = Time.unscaledTime + 1.5f;
            _nextLeakTime = Time.unscaledTime + 3f;
            _nextPressureTime = Time.unscaledTime + 1f;
            _nextPressureClearTime = Time.unscaledTime + 5f;

            ActiveContext = _context;
        }

        private void SubscribeHandlers()
        {
            ISubscriptionScope root = _context.RootScope;
            root.Add(_valueBus, OnValueChanged);
            root.Add(_assetBus, OnAssetLoaded);
            root.Add(_metricBus, OnMetricTick);
            root.Add(_burstBus, OnBurstEvent);

            // A scoped group released mid-play demonstrates scope disposal and subscriber removal.
            var sceneScope = _context.CreateScope();
            sceneScope.Add(_valueBus, OnValueChangedScoped);
            sceneScope.Add(_burstBus, OnBurstEventScoped);

            if (enableFaultySubscriber)
            {
                _context.RootScope.Add(_metricBus, OnMetricTickFaulty);
            }

            if (enableReentrancyDemo)
            {
                _context.RootScope.Add(_burstBus, OnBurstReentrantDemo);
            }
        }

        private void StartProducer()
        {
            _producerThread = new Thread(ProducerLoop)
            {
                IsBackground = true,
                Name = "EventBusSample.Producer",
            };
            _producerThread.Start();
        }

        private void ProducerLoop()
        {
            // Fixed-rate producer: enqueues into the bounded ring; a full ring applies backpressure
            // by dropping at the producer side, which is exactly what RejectedCount visualizes.
            int periodMs = Mathf.Max(1, 1000 / Mathf.Max(1, backgroundEventsPerSecond));
            while (!_producerStop)
            {
                var evt = new SampleAssetLoadedEvent
                {
                    AssetId = Interlocked.Increment(ref _assetIdCounter),
                    TimestampTicks = DateTime.UtcNow.Ticks,
                };
                _assetQueue.TryEnqueue(in evt);
                Thread.Sleep(periodMs);
            }
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            for (int index = 0; index < directPublishesPerFrame; index++)
            {
                _valueBus.Publish(new SampleValueChangedEvent { Id = index, NewValue = now });
            }

            for (int index = 0; index < streamWritesPerFrame; index++)
            {
                _metricStream.TryWrite(new SampleMetricTickEvent
                {
                    Sequence = _metricSequence++,
                    Value = Mathf.Sin(now + index),
                });
            }

            if (now >= _nextBurstTime)
            {
                _nextBurstTime = now + 1f;
                int burstId = _burstCounter++;
                for (int index = 0; index < burstSize; index++)
                {
                    _burstBus.Publish(new SampleBurstEvent { BurstId = burstId, IndexInBurst = index });
                }
            }

            if (enableFaultySubscriber && now >= _nextFaultTime)
            {
                _nextFaultTime = now + 2f;
                try
                {
                    _metricBus.Publish(new SampleMetricTickEvent { Sequence = -1, Value = -1f });
                }
                catch (InvalidOperationException)
                {
                    // Expected: ContinueOnError rethrows the injected fault after the round.
                    // It is already counted in SubscriberErrorCount and logged through the sink.
                }
            }

            if (enableChurn && now >= _nextChurnTime)
            {
                _nextChurnTime = now + 1.5f;
                _churnScope?.Dispose();
                _churnScope = new SubscriptionScope();
                for (int index = 0; index < 8; index++)
                {
                    _churnScope.Add(_burstBus, OnBurstChurn);
                }
            }

            if (enableLeakDemo && now >= _nextLeakTime)
            {
                _nextLeakTime = now + 6f;
                var leakHost = new GameObject("[EventBus Sample] LeakDemo");
                leakHost.AddComponent<SampleLeakDemoSubscriber>().Arm(_valueBus, 2f);
            }

            if (enableReentrancyDemo && now >= _nextReentrantTime)
            {
                _nextReentrantTime = now + 5f;

                // Arm for exactly one outer publish: the demo handler republishes its own event
                // type until the depth ceiling drops one publish, then the flag disarms as the
                // chain unwinds.
                _reentrantDemoActive = true;
                _burstBus.Publish(new SampleBurstEvent { BurstId = -1, IndexInBurst = 0 });
                _reentrantDemoActive = false;
            }

            if (enableBackpressureDemo)
            {
                if (now >= _nextPressureTime)
                {
                    _nextPressureTime = now + 1f;
                    // Overflow the 64-slot stream: the surplus writes increment RejectedCount.
                    for (int index = 0; index < burstSize; index++)
                    {
                        _pressureStream.TryWrite(new SampleBurstEvent { BurstId = -2, IndexInBurst = index });
                    }
                }

                if (now >= _nextPressureClearTime)
                {
                    _nextPressureClearTime = now + 5f;
                    // Discarding the buffered events unread increments DroppedCount (confirmed loss),
                    // distinguishing it in the panel from the refused writes above.
                    _pressureStream.Clear();
                }
            }

            _pump.Drain(
                Mathf.Max(0, maxEventsPerTargetPerFrame),
                maxEventsPerFrame > 0 ? maxEventsPerFrame : int.MaxValue);
        }

        private void OnDestroy()
        {
            ActiveContext = null;

            _producerStop = true;
            if (_producerThread != null && _producerThread.IsAlive)
            {
                _assetQueue.Close();
                _producerThread.Join(2000);
            }

            _churnScope?.Dispose();
            _pump?.Clear();
            _context?.Dispose();
            _context = null;
        }

        // ------------------------------------------------------------------ handlers

        private void OnValueChanged(SampleValueChangedEvent evt) { }
        private void OnValueChangedScoped(SampleValueChangedEvent evt) { }
        private void OnAssetLoaded(SampleAssetLoadedEvent evt) { }
        private void OnMetricTick(SampleMetricTickEvent evt) { }
        private void OnBurstEvent(SampleBurstEvent evt) { }
        private void OnBurstEventScoped(SampleBurstEvent evt) { }
        private void OnBurstChurn(SampleBurstEvent evt) { }

        private void OnMetricTickFaulty(SampleMetricTickEvent evt)
        {
            // Only the injected fault tick throws; normal ticks pass through.
            if (evt.Sequence < 0)
            {
                throw new InvalidOperationException("Sample fault injection: a subscriber threw on purpose.");
            }
        }

        private void OnBurstReentrantDemo(SampleBurstEvent evt)
        {
            if (!_reentrantDemoActive)
            {
                return;
            }

            // Republish the same event type: each nested publish consumes one depth level until
            // the ceiling drops it, which is what DroppedReentrantCount and the warning banner
            // visualize. Bounded by the ceiling; the bus stays consistent throughout.
            _burstBus.Publish(evt);
        }
    }

    /// <summary>
    /// Subscribes and then destroys itself without unsubscribing. The debugger's destroyed-target
    /// detection is expected to flag this row — it exists to demonstrate the leak warning.
    /// </summary>
    public sealed class SampleLeakDemoSubscriber : MonoBehaviour
    {
        private float _destroyAfter = 2f;

        public void Arm(EventBus<SampleValueChangedEvent> bus, float destroyAfter)
        {
            _destroyAfter = destroyAfter;
            bus.Subscribe(OnValueChanged);
            Destroy(gameObject, _destroyAfter);
        }

        private void OnValueChanged(SampleValueChangedEvent evt) { }
    }
}
