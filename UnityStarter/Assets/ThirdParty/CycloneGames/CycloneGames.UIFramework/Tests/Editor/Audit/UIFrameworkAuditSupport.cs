using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor.Audit
{
    /// <summary>
    /// Dimension constants for the UIFramework audit matrix. Index values are used as the
    /// matrix coordinates so pairwise generation, prediction and reporting stay in sync.
    /// </summary>
    internal static class AuditDims
    {
        public const int DimensionCount = 8;

        public const int SourcePrefabReference = 0;
        public const int SourceAssetReference = 1;
        public const int SourcePathLocation = 2;

        public const int BindersNone = 0;
        public const int BindersSingle = 1;
        public const int BindersThreeWithFailingSecond = 2;

        public const int TransitionNone = 0;
        public const int TransitionSyncSuccess = 1;
        public const int TransitionThrowOnOpen = 2;
        public const int TransitionCancelOnOpen = 3;

        public const int OptionsPlain = 0;
        public const int OptionsContext = 1;
        public const int OptionsOpenerWithContext = 2;
        public const int OptionsOpenerSuppressed = 3;
        public const int OptionsSceneBoundOverrideTrue = 4;
        public const int OptionsSceneBoundOverrideFalse = 5;

        public const int NavigationDisabled = 0;
        public const int NavigationEnabled = 1;

        public const int CanvasInherit = 0;
        public const int CanvasIsolated = 1;

        public const int CullDisabled = 0;
        public const int CullEnabled = 1;

        public const int CapacityBoundarySingleSlot = 0;
        public const int CapacityRelaxed = 1;

        /// <summary>Window id used as the only matrix target.</summary>
        public const string TargetWindowId = "AuditTarget";

        /// <summary>Window id used as the pre-opened opener when a tuple asks for one.</summary>
        public const string OpenerWindowId = "AuditOpener";

        public static int DomainSize(int dimension)
        {
            switch (dimension)
            {
                case 0: return 3;
                case 1: return 3;
                case 2: return 4;
                case 3: return 6;
                case 4: return 2;
                case 5: return 2;
                case 6: return 2;
                case 7: return 2;
                default: throw new ArgumentOutOfRangeException(nameof(dimension));
            }
        }

        public static string DimensionName(int dimension)
        {
            switch (dimension)
            {
                case 0: return "D1.Source";
                case 1: return "D2.Binders";
                case 2: return "D3.Transition";
                case 3: return "D4.OpenOptions";
                case 4: return "D5.Navigation";
                case 5: return "D6.Canvas";
                case 6: return "D7.CullWhenHidden";
                case 7: return "D8.Capacity";
                default: throw new ArgumentOutOfRangeException(nameof(dimension));
            }
        }

        public static string SourceName(int value)
        {
            switch (value)
            {
                case SourcePrefabReference: return "PrefabReference";
                case SourceAssetReference: return "AssetReference";
                case SourcePathLocation: return "PathLocation";
                default: return "Source?" + value;
            }
        }

        public static string BindersName(int value)
        {
            switch (value)
            {
                case BindersNone: return "None";
                case BindersSingle: return "Single";
                case BindersThreeWithFailingSecond: return "ThreeWithFailingSecond";
                default: return "Binders?" + value;
            }
        }

        public static string TransitionName(int value)
        {
            switch (value)
            {
                case TransitionNone: return "Null";
                case TransitionSyncSuccess: return "SyncSuccess";
                case TransitionThrowOnOpen: return "ThrowOnOpen";
                case TransitionCancelOnOpen: return "CancelOnOpen";
                default: return "Transition?" + value;
            }
        }

        public static string OptionsName(int value)
        {
            switch (value)
            {
                case OptionsPlain: return "Plain";
                case OptionsContext: return "Context";
                case OptionsOpenerWithContext: return "OpenerWithContext";
                case OptionsOpenerSuppressed: return "OpenerSuppressed";
                case OptionsSceneBoundOverrideTrue: return "SceneBoundOverrideTrue";
                case OptionsSceneBoundOverrideFalse: return "SceneBoundOverrideFalse";
                default: return "Options?" + value;
            }
        }

        public static string NavigationName(int value)
        {
            return value == NavigationEnabled ? "Enabled" : "Disabled";
        }

        public static string CanvasName(int value)
        {
            return value == CanvasIsolated ? "IsolatedCanvas" : "InheritLayerCanvas";
        }

        public static string CullName(int value)
        {
            return value == CullEnabled ? "Cull" : "NoCull";
        }

        public static string CapacityName(int value)
        {
            return value == CapacityBoundarySingleSlot
                ? "Boundary(MaxActive=1,MaxInstantiatesPerFrame=1)"
                : "Relaxed(MaxActive=8,MaxInstantiatesPerFrame=2)";
        }

        public static int ValueCount(int dimension)
        {
            return DomainSize(dimension);
        }
    }

    /// <summary>
    /// One point of the audit matrix. Field order matches the D1..D8 dimension order so the
    /// tuple can be flattened into the coverage buffer without a translation table.
    /// </summary>
    internal struct AuditTuple
    {
        public int Source;
        public int Binders;
        public int Transition;
        public int Options;
        public int Navigation;
        public int Canvas;
        public int Cull;
        public int Capacity;

        public AuditTuple(
            int source,
            int binders,
            int transition,
            int options,
            int navigation,
            int canvas,
            int cull,
            int capacity)
        {
            Source = source;
            Binders = binders;
            Transition = transition;
            Options = options;
            Navigation = navigation;
            Canvas = canvas;
            Cull = cull;
            Capacity = capacity;
        }

        public void WriteValues(int[] buffer)
        {
            buffer[0] = Source;
            buffer[1] = Binders;
            buffer[2] = Transition;
            buffer[3] = Options;
            buffer[4] = Navigation;
            buffer[5] = Canvas;
            buffer[6] = Cull;
            buffer[7] = Capacity;
        }

        /// <summary>True when the tuple needs a pre-opened opener window to be meaningful.</summary>
        public bool RequiresPreOpenedOpener()
        {
            return Options == AuditDims.OptionsOpenerWithContext ||
                   Options == AuditDims.OptionsOpenerSuppressed;
        }

        /// <summary>
        /// Transition shape actually observed by the service. SuppressWindowTransition=true
        /// forces the session driver to null, so every driver variant becomes a no-op.
        /// </summary>
        public int EffectiveTransition()
        {
            return Options == AuditDims.OptionsOpenerSuppressed
                ? AuditDims.TransitionNone
                : Transition;
        }

        public string Describe()
        {
            return "D1=" + AuditDims.SourceName(Source) +
                   "|D2=" + AuditDims.BindersName(Binders) +
                   "|D3=" + AuditDims.TransitionName(Transition) +
                   "|D4=" + AuditDims.OptionsName(Options) +
                   "|D5=" + AuditDims.NavigationName(Navigation) +
                   "|D6=" + AuditDims.CanvasName(Canvas) +
                   "|D7=" + AuditDims.CullName(Cull) +
                   "|D8=" + AuditDims.CapacityName(Capacity);
        }
    }

    internal enum AuditExpectedOutcome
    {
        Success = 0,
        CapacityRejected = 1,
        BinderRejected = 2,
        TransitionThrew = 3,
        TransitionCanceled = 4,
    }

    internal static class AuditPredictor
    {
        /// <summary>
        /// Derives the expected outcome from the tuple alone. Order matters: the capacity
        /// guard runs before any asset work, the binder stage runs before the transition.
        /// </summary>
        public static AuditExpectedOutcome Predict(AuditTuple tuple)
        {
            if (tuple.Capacity == AuditDims.CapacityBoundarySingleSlot && tuple.RequiresPreOpenedOpener())
            {
                return AuditExpectedOutcome.CapacityRejected;
            }

            if (tuple.Binders == AuditDims.BindersThreeWithFailingSecond)
            {
                return AuditExpectedOutcome.BinderRejected;
            }

            int transition = tuple.EffectiveTransition();
            if (transition == AuditDims.TransitionThrowOnOpen)
            {
                return AuditExpectedOutcome.TransitionThrew;
            }

            if (transition == AuditDims.TransitionCancelOnOpen)
            {
                return AuditExpectedOutcome.TransitionCanceled;
            }

            return AuditExpectedOutcome.Success;
        }
    }

    /// <summary>
    /// Deterministic pairwise generator. No LINQ, no dynamic code: the case list is computed
    /// once from a greedy set cover over every two-dimensional value combination and then
    /// padded with extra tuples until the requested minimum case count is reached.
    /// </summary>
    internal static class AuditPairwise
    {
        public const int MinimumRequiredCases = 48;

        private const int GeneratorSeed = 20240607;

        private static List<AuditTuple> _cachedCases;
        private static int[,] _pairOffset;
        private static int _pairSlotCount;

        public static List<AuditTuple> GetCases()
        {
            if (_cachedCases == null)
            {
                _cachedCases = Build();
            }

            return _cachedCases;
        }

        /// <summary>
        /// Runs the generator again without touching the cache so determinism can be asserted.
        /// </summary>
        public static List<AuditTuple> Rebuild()
        {
            return Build();
        }

        public static int PairCount()
        {
            EnsureLayout();
            return _pairSlotCount;
        }

        /// <summary>
        /// Verifies that every (dimension, value) pair appears in at least one case.
        /// Returns false and reports the first missing combination when coverage is broken.
        /// </summary>
        public static bool VerifyCoverage(List<AuditTuple> cases, out string missing)
        {
            EnsureLayout();
            bool[] covered = new bool[_pairSlotCount];
            int[] values = new int[AuditDims.DimensionCount];
            for (int c = 0; c < cases.Count; c++)
            {
                cases[c].WriteValues(values);
                MarkCovered(covered, values);
            }

            for (int i = 0; i < AuditDims.DimensionCount; i++)
            {
                for (int j = i + 1; j < AuditDims.DimensionCount; j++)
                {
                    int domainJ = AuditDims.DomainSize(j);
                    int offset = _pairOffset[i, j];
                    for (int a = 0; a < AuditDims.DomainSize(i); a++)
                    {
                        for (int b = 0; b < domainJ; b++)
                        {
                            if (!covered[offset + a * domainJ + b])
                            {
                                missing = AuditDims.DimensionName(i) + "=" + a +
                                          " x " + AuditDims.DimensionName(j) + "=" + b;
                                return false;
                            }
                        }
                    }
                }
            }

            missing = null;
            return true;
        }

        private static List<AuditTuple> Build()
        {
            EnsureLayout();
            List<AuditTuple> universe = EnumerateAllTuples();
            List<int> order = ShuffledOrder(universe.Count);
            bool[] covered = new bool[_pairSlotCount];
            bool[] picked = new bool[universe.Count];
            List<AuditTuple> cases = new List<AuditTuple>(MinimumRequiredCases + 16);
            int[] values = new int[AuditDims.DimensionCount];

            int remaining = _pairSlotCount;
            while (remaining > 0)
            {
                int bestIndex = -1;
                int bestGain = 0;
                for (int k = 0; k < order.Count; k++)
                {
                    int candidateIndex = order[k];
                    if (picked[candidateIndex])
                    {
                        continue;
                    }

                    universe[candidateIndex].WriteValues(values);
                    int gain = CountGain(covered, values);
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestIndex = candidateIndex;
                    }
                }

                if (bestIndex < 0 || bestGain == 0)
                {
                    // Unreachable for a well formed domain; break out instead of spinning.
                    break;
                }

                universe[bestIndex].WriteValues(values);
                remaining -= MarkCovered(covered, values);
                picked[bestIndex] = true;
                cases.Add(universe[bestIndex]);
            }

            // Pad with deterministically chosen extra tuples: they add no pairwise value but
            // keep the matrix wide enough for higher-order combinations to show up.
            for (int k = 0; k < order.Count && cases.Count < MinimumRequiredCases; k++)
            {
                int candidateIndex = order[k];
                if (picked[candidateIndex])
                {
                    continue;
                }

                picked[candidateIndex] = true;
                cases.Add(universe[candidateIndex]);
            }

            return cases;
        }

        private static void EnsureLayout()
        {
            if (_pairOffset != null)
            {
                return;
            }

            _pairOffset = new int[AuditDims.DimensionCount, AuditDims.DimensionCount];
            int cursor = 0;
            for (int i = 0; i < AuditDims.DimensionCount; i++)
            {
                for (int j = i + 1; j < AuditDims.DimensionCount; j++)
                {
                    _pairOffset[i, j] = cursor;
                    cursor += AuditDims.DomainSize(i) * AuditDims.DomainSize(j);
                }
            }

            _pairSlotCount = cursor;
        }

        private static int CountGain(bool[] covered, int[] values)
        {
            int gain = 0;
            for (int i = 0; i < AuditDims.DimensionCount; i++)
            {
                for (int j = i + 1; j < AuditDims.DimensionCount; j++)
                {
                    int domainJ = AuditDims.DomainSize(j);
                    int slot = _pairOffset[i, j] + values[i] * domainJ + values[j];
                    if (!covered[slot])
                    {
                        gain++;
                    }
                }
            }

            return gain;
        }

        private static int MarkCovered(bool[] covered, int[] values)
        {
            int marked = 0;
            for (int i = 0; i < AuditDims.DimensionCount; i++)
            {
                for (int j = i + 1; j < AuditDims.DimensionCount; j++)
                {
                    int domainJ = AuditDims.DomainSize(j);
                    int slot = _pairOffset[i, j] + values[i] * domainJ + values[j];
                    if (!covered[slot])
                    {
                        covered[slot] = true;
                        marked++;
                    }
                }
            }

            return marked;
        }

        private static List<AuditTuple> EnumerateAllTuples()
        {
            List<AuditTuple> tuples = new List<AuditTuple>(4096);
            for (int d0 = 0; d0 < AuditDims.DomainSize(0); d0++)
            {
                for (int d1 = 0; d1 < AuditDims.DomainSize(1); d1++)
                {
                    for (int d2 = 0; d2 < AuditDims.DomainSize(2); d2++)
                    {
                        for (int d3 = 0; d3 < AuditDims.DomainSize(3); d3++)
                        {
                            for (int d4 = 0; d4 < AuditDims.DomainSize(4); d4++)
                            {
                                for (int d5 = 0; d5 < AuditDims.DomainSize(5); d5++)
                                {
                                    for (int d6 = 0; d6 < AuditDims.DomainSize(6); d6++)
                                    {
                                        for (int d7 = 0; d7 < AuditDims.DomainSize(7); d7++)
                                        {
                                            tuples.Add(new AuditTuple(d0, d1, d2, d3, d4, d5, d6, d7));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return tuples;
        }

        private static List<int> ShuffledOrder(int count)
        {
            List<int> order = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                order.Add(i);
            }

            var random = new System.Random(GeneratorSeed);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int swap = random.Next(i + 1);
                int value = order[i];
                order[i] = order[swap];
                order[swap] = value;
            }

            return order;
        }
    }

    /// <summary>Builds the concrete <see cref="UIOpenOptions"/> for a D4 value.</summary>
    internal static class AuditOptions
    {
        public static UIOpenOptions Create(int variant, object contextPayload, IUIWindowTransitionDriver driver)
        {
            switch (variant)
            {
                case AuditDims.OptionsPlain:
                    return new UIOpenOptions(null, null, null, default, driver, false);
                case AuditDims.OptionsContext:
                    return new UIOpenOptions(null, contextPayload, null, default, driver, false);
                case AuditDims.OptionsOpenerWithContext:
                    return new UIOpenOptions(
                        AuditDims.OpenerWindowId, contextPayload, null, default, driver, false);
                case AuditDims.OptionsOpenerSuppressed:
                    return new UIOpenOptions(
                        AuditDims.OpenerWindowId, null, null, default, driver, true);
                case AuditDims.OptionsSceneBoundOverrideTrue:
                    return new UIOpenOptions(null, contextPayload, true, default, driver, false);
                case AuditDims.OptionsSceneBoundOverrideFalse:
                    return new UIOpenOptions(null, null, false, default, driver, false);
                default:
                    throw new ArgumentOutOfRangeException(nameof(variant));
            }
        }
    }

    /// <summary>
    /// Provider lease that reports acquisition/disposal back to the audit provider so the
    /// "exactly one dispose per acquisition" invariant is measurable in O(1) per iteration.
    /// </summary>
    internal sealed class AuditLease<TAsset> : IUIAssetLease<TAsset> where TAsset : UnityEngine.Object
    {
        private readonly AuditAssetProvider _owner;
        private readonly bool _isConfiguration;

        public AuditLease(TAsset asset, AuditAssetProvider owner, bool isConfiguration)
        {
            Asset = asset;
            _owner = owner;
            _isConfiguration = isConfiguration;
        }

        public TAsset Asset { get; }

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (_owner != null)
            {
                _owner.NotifyLeaseDisposed(_isConfiguration, DisposeCount);
            }
        }
    }

    /// <summary>
    /// Deterministic, synchronous asset provider. Every acquisition creates a fresh lease so
    /// the service is forced to release each lease exactly once.
    /// </summary>
    internal sealed class AuditAssetProvider : IUIWindowAssetProvider
    {
        private readonly Dictionary<string, UIWindowConfiguration> _configurations =
            new Dictionary<string, UIWindowConfiguration>(16, StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> _prefabs =
            new Dictionary<string, GameObject>(16, StringComparer.Ordinal);

        public bool GateConfigurationAcquisition;
        public bool GatePrefabAcquisition;
        public bool IgnoreCancellation;
        public UniTaskCompletionSource<IUIAssetLease<UIWindowConfiguration>> ConfigurationGate;
        public UniTaskCompletionSource<IUIAssetLease<GameObject>> PrefabGate;

        public int ConfigurationAcquireCount { get; private set; }
        public int PrefabAcquireCount { get; private set; }
        public int ConfigurationDisposeCount { get; private set; }
        public int PrefabDisposeCount { get; private set; }
        public int DoubleDisposeCount { get; private set; }
        public int MissingConfigurationCount { get; private set; }
        public int MissingPrefabCount { get; private set; }
        public string LastConfigurationWindowId { get; private set; }
        public UIAssetReference LastPrefabReference { get; private set; }

        /// <summary>Leases acquired but not yet disposed. A live session owns at most one each.</summary>
        public int ConfigurationOutstanding
        {
            get { return ConfigurationAcquireCount - ConfigurationDisposeCount; }
        }

        public int PrefabOutstanding
        {
            get { return PrefabAcquireCount - PrefabDisposeCount; }
        }

        /// <summary>
        /// Maps a window id to its configuration and, under the runtime location the harness
        /// writes into that configuration, to its prefab. AssetReference and PathLocation both
        /// resolve to the same "UI/{windowId}" location, so one map serves both sources.
        /// </summary>
        public void Map(string windowId, UIWindowConfiguration configuration, GameObject prefab)
        {
            if (configuration != null)
            {
                _configurations[windowId] = configuration;
            }

            if (prefab != null)
            {
                _prefabs[PrefabLocation(windowId)] = prefab;
            }
        }

        public static string PrefabLocation(string windowId)
        {
            return "UI/" + windowId;
        }

        public void ResetCounters()
        {
            ConfigurationAcquireCount = 0;
            PrefabAcquireCount = 0;
            ConfigurationDisposeCount = 0;
            PrefabDisposeCount = 0;
            DoubleDisposeCount = 0;
            MissingConfigurationCount = 0;
            MissingPrefabCount = 0;
            LastConfigurationWindowId = null;
            LastPrefabReference = default;
        }

        internal void NotifyLeaseDisposed(bool isConfiguration, int disposeCount)
        {
            if (disposeCount > 1)
            {
                DoubleDisposeCount++;
            }

            if (isConfiguration)
            {
                ConfigurationDisposeCount++;
            }
            else
            {
                PrefabDisposeCount++;
            }
        }

        public UniTask<IUIAssetLease<UIWindowConfiguration>> AcquireConfigurationAsync(
            string windowId,
            UIAssetLoadContext context,
            CancellationToken cancellationToken)
        {
            ConfigurationAcquireCount++;
            LastConfigurationWindowId = windowId;

            if (GateConfigurationAcquisition && ConfigurationGate != null)
            {
                return IgnoreCancellation
                    ? ConfigurationGate.Task
                    : ConfigurationGate.Task.AttachExternalCancellation(cancellationToken);
            }

            UIWindowConfiguration configuration;
            if (string.IsNullOrEmpty(windowId) || !_configurations.TryGetValue(windowId, out configuration))
            {
                MissingConfigurationCount++;
                return Faulted<IUIAssetLease<UIWindowConfiguration>>(new InvalidOperationException(
                    "Audit provider has no configuration mapping for '" + windowId + "'."));
            }

            var lease = new AuditLease<UIWindowConfiguration>(configuration, this, true);
            return UniTask.FromResult<IUIAssetLease<UIWindowConfiguration>>(lease);
        }

        public UniTask<IUIAssetLease<GameObject>> AcquirePrefabAsync(
            UIAssetReference reference,
            UIAssetLoadContext context,
            CancellationToken cancellationToken)
        {
            PrefabAcquireCount++;
            LastPrefabReference = reference;

            if (GatePrefabAcquisition && PrefabGate != null)
            {
                return IgnoreCancellation
                    ? PrefabGate.Task
                    : PrefabGate.Task.AttachExternalCancellation(cancellationToken);
            }

            GameObject prefab;
            string location = reference.Location;
            if (string.IsNullOrEmpty(location) || !_prefabs.TryGetValue(location, out prefab))
            {
                MissingPrefabCount++;
                return Faulted<IUIAssetLease<GameObject>>(new InvalidOperationException(
                    "Audit provider has no prefab mapping for '" + location + "'."));
            }

            var lease = new AuditLease<GameObject>(prefab, this, false);
            return UniTask.FromResult<IUIAssetLease<GameObject>>(lease);
        }

        private static UniTask<T> Faulted<T>(Exception exception)
        {
            var source = new UniTaskCompletionSource<T>();
            source.TrySetException(exception);
            return source.Task;
        }
    }

    /// <summary>
    /// Recording binder with per-window fault injection. Faults are scoped to a single window
    /// id so a throw-on-bind binder cannot poison the pre-opened opener window.
    /// </summary>
    internal sealed class AuditBinder : IUIWindowBinder
    {
        private readonly string _name;

        public AuditBinder(string name)
        {
            _name = name;
            Events = new List<string>(8);
            ThrowOnStage = -1;
            SelfCloseOnStage = -1;
            RecordEvents = true;
        }

        public string Name { get { return _name; } }
        public List<string> Events { get; }

        /// <summary>
        /// Disable for long running loops: the event log is unbounded and would otherwise be
        /// counted as retained memory by the soak growth assertion.
        /// </summary>
        public bool RecordEvents;

        public bool ThrowOnBind;
        public bool ThrowOnDispose;
        public int ThrowOnStage;
        public string ThrowForWindowId;
        public int SelfCloseOnStage;
        public int FinishedCloseStateValue;

        public int BindCount { get; private set; }

        /// <summary>
        /// Bind calls that faulted before a binding object existed. A faulted Bind can never be
        /// disposed, so the disposal invariant is DisposeCount + BindFaultCount == BindCount.
        /// </summary>
        public int BindFaultCount { get; private set; }

        public int DisposeCount { get; private set; }
        public int CallbackCount { get; private set; }
        public int SelfCloseGuardHitCount { get; private set; }
        public UIWindow LastBoundWindow { get; private set; }

        public IUIWindowBinding Bind(UIWindowBindingContext context)
        {
            BindCount++;
            LastBoundWindow = context.Window;
            if (RecordEvents)
            {
                Events.Add(_name + ":Bind:" + context.Window.WindowId);
            }

            if (ThrowOnBind && Matches(ThrowForWindowId, context.Window.WindowId))
            {
                BindFaultCount++;
                throw new InvalidOperationException(_name + " bind failed.");
            }

            return new Binding(this, context.UIService, context.Window);
        }

        private static bool Matches(string filter, string windowId)
        {
            return filter == null || string.Equals(filter, windowId, StringComparison.Ordinal);
        }

        private sealed class Binding : IUIWindowBinding
        {
            private AuditBinder _owner;
            private readonly IUIService _uiService;
            private readonly UIWindow _window;
            private readonly string _windowId;

            public Binding(AuditBinder owner, IUIService uiService, UIWindow window)
            {
                _owner = owner;
                _uiService = uiService;
                _window = window;
                _windowId = window != null ? window.WindowId : string.Empty;
            }

            public void OnWindowStateChanged(WindowStateCallbackType state)
            {
                AuditBinder owner = _owner;
                if (owner == null)
                {
                    return;
                }

                owner.CallbackCount++;
                if (owner.RecordEvents)
                {
                    owner.Events.Add(owner.Name + ":" + state + ":" + _windowId);
                }

                if (state == WindowStateCallbackType.OnFinishedClose && _window != null)
                {
                    owner.FinishedCloseStateValue = (int)_window.State;
                }

                if (owner.ThrowOnStage == (int)state && Matches(owner.ThrowForWindowId, _windowId))
                {
                    throw new InvalidOperationException(
                        owner.Name + " callback " + state + " failed for '" + _windowId + "'.");
                }

                if (owner.SelfCloseOnStage == (int)state && Matches(owner.SelfCloseForTarget(), _windowId))
                {
                    try
                    {
                        UniTask<bool> reentrant = _uiService.CloseAsync(_windowId);
                        reentrant.Forget();
                    }
                    catch (InvalidOperationException)
                    {
                        owner.SelfCloseGuardHitCount++;
                    }
                }
            }

            public void Dispose()
            {
                AuditBinder owner = _owner;
                if (owner == null)
                {
                    return;
                }

                _owner = null;
                owner.DisposeCount++;
                if (owner.RecordEvents)
                {
                    owner.Events.Add(owner.Name + ":Dispose:" + _windowId);
                }

                if (owner.ThrowOnDispose)
                {
                    throw new InvalidOperationException(owner.Name + " dispose failed.");
                }
            }
        }

        private string SelfCloseForTarget()
        {
            return ThrowForWindowId;
        }
    }

    /// <summary>Synchronous transition driver with fault injection and an optional close gate.</summary>
    internal sealed class AuditTransitionDriver : IUIWindowTransitionDriver
    {
        public string OpenThrowMessage;
        public string CloseThrowMessage;
        public bool CancelOpen;
        public bool CancelClose;
        public UniTaskCompletionSource CloseGate;

        public int OpenCallCount { get; private set; }
        public int CloseCallCount { get; private set; }

        public UniTask PlayOpenAsync(UIWindow window, CancellationToken cancellationToken)
        {
            OpenCallCount++;
            if (OpenThrowMessage != null)
            {
                throw new InvalidOperationException(OpenThrowMessage);
            }

            if (CancelOpen)
            {
                return UniTask.FromCanceled(new CancellationToken(canceled: true));
            }

            return UniTask.CompletedTask;
        }

        public UniTask PlayCloseAsync(UIWindow window, CancellationToken cancellationToken)
        {
            CloseCallCount++;
            if (CloseThrowMessage != null)
            {
                throw new InvalidOperationException(CloseThrowMessage);
            }

            if (CancelClose)
            {
                return UniTask.FromCanceled(new CancellationToken(canceled: true));
            }

            if (CloseGate != null)
            {
                // Deliberately not cancellation-aware: the test owns release timing.
                return CloseGate.Task;
            }

            return UniTask.CompletedTask;
        }
    }

    /// <summary>Coordinated-transition stub with fault injection and an optional gate.</summary>
    internal sealed class AuditCoordinator : IUITransitionCoordinator
    {
        public string ThrowMessage;
        public bool Cancel;
        public UniTaskCompletionSource Gate;

        public int CallCount { get; private set; }
        public UIWindow Leaving { get; private set; }
        public UIWindow Entering { get; private set; }
        public NavigationDirection Direction { get; private set; }

        public UniTask TransitionAsync(
            UIWindow leaving,
            UIWindow entering,
            NavigationDirection direction,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Leaving = leaving;
            Entering = entering;
            Direction = direction;

            if (ThrowMessage != null)
            {
                throw new InvalidOperationException(ThrowMessage);
            }

            if (Cancel)
            {
                return UniTask.FromCanceled(new CancellationToken(canceled: true));
            }

            if (Gate != null)
            {
                return Gate.Task;
            }

            return UniTask.CompletedTask;
        }
    }

    /// <summary>Minimal presenter used to measure <see cref="UIPresenterBinder"/> binding cost.</summary>
    internal sealed class AuditPresenter : IUIPresenter
    {
        public int DisposeCount { get; private set; }
        public UIWindow View { get; private set; }
        public IUIService Service { get; private set; }

        public void SetView(UIWindow view)
        {
            View = view;
        }

        public void SetUIService(IUIService uiService)
        {
            Service = uiService;
        }

        public void OnViewOpening()
        {
        }

        public void OnViewOpened()
        {
        }

        public void OnViewClosing()
        {
        }

        public void OnViewClosed()
        {
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    /// <summary>
    /// Owns the Unity scene fixture plus every service created for one audit case so a case
    /// can be torn down deterministically without touching shared state.
    /// </summary>
    internal sealed class AuditHarness : IDisposable
    {
        private readonly List<UIService> _services = new List<UIService>(4);
        private readonly UIRuntimeTestFixture _fixture;

        public AuditHarness()
        {
            _fixture = new UIRuntimeTestFixture("Main");
        }

        public UIRuntimeTestFixture Fixture { get { return _fixture; } }
        public UIRoot Root { get { return _fixture.Root; } }
        public UILayer Layer { get { return _fixture.Layer; } }

        public UINavigationService CreateNavigation(int initialCapacity)
        {
            return new UINavigationService(initialCapacity);
        }

        public AuditAssetProvider CreateProvider()
        {
            return new AuditAssetProvider();
        }

        public UIService CreateService(
            AuditAssetProvider provider,
            UIServiceOptions options,
            IReadOnlyList<IUIWindowBinder> binders)
        {
            var service = new UIService(_fixture.Root, provider, options, binders);
            _services.Add(service);
            return service;
        }

        public UIWindowConfiguration CreateDirect(string windowId, int priority, bool sceneBound)
        {
            return _fixture.CreateDirectConfiguration(windowId, priority, sceneBound);
        }

        public UIWindowConfiguration CreateProviderConfiguration(string windowId, int source, int priority)
        {
            UIWindowConfiguration configuration =
                _fixture.CreateAddressConfiguration(windowId, "UI/" + windowId, priority);
            if (source == AuditDims.SourcePathLocation)
            {
                TestReflection.SetField(
                    configuration,
                    "source",
                    UIWindowConfiguration.PrefabSource.PathLocation);
                TestReflection.SetField(configuration, "prefabLocation", "UI/" + windowId);
            }

            return configuration;
        }

        public GameObject CreatePrefab(string windowId)
        {
            return _fixture.CreateWindowPrefab(windowId);
        }

        public static void ApplyCanvasPolicy(UIWindowConfiguration configuration, int canvas)
        {
            TestReflection.SetField(
                configuration,
                "subCanvasPolicy",
                canvas == AuditDims.CanvasIsolated
                    ? UIWindowConfiguration.SubCanvasPolicy.IsolatedCanvas
                    : UIWindowConfiguration.SubCanvasPolicy.InheritLayerCanvas);
        }

        public static void ApplyCullWhenHidden(UIWindowConfiguration configuration, int cull)
        {
            TestReflection.SetField(configuration, "cullWhenHidden", cull == AuditDims.CullEnabled);
        }

        public static UIServiceOptions CreateOptions(int capacityProfile, IUINavigationService navigation)
        {
            var options = new UIServiceOptions();
            options.NavigationService = navigation;
            if (capacityProfile == AuditDims.CapacityBoundarySingleSlot)
            {
                options.InitialWindowCapacity = 1;
                options.MaxActiveWindows = 1;
                options.MaxInstantiatesPerFrame = 1;
            }
            else
            {
                options.InitialWindowCapacity = 8;
                options.MaxActiveWindows = 8;
                options.MaxInstantiatesPerFrame = 2;
            }

            return options;
        }

        /// <summary>Counts live window GameObjects physically parented under the layer.</summary>
        public static int CountAttachedWindowObjects(UILayer layer)
        {
            Transform parent = layer.transform;
            int count = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).GetComponent<UIWindow>() != null)
                {
                    count++;
                }
            }

            return count;
        }

        public void Dispose()
        {
            for (int i = _services.Count - 1; i >= 0; i--)
            {
                UIService service = _services[i];
                if (service == null || service.IsDisposed)
                {
                    continue;
                }

                try
                {
                    service.Dispose();
                }
                catch (Exception exception)
                {
                    // Teardown must never mask the assertion that already failed. Keep the
                    // detail in the log instead of replacing the original exception.
                    TestContext.Progress.WriteLine(
                        "AUDIT-TEARDOWN: UIService.Dispose threw " + exception.GetType().Name +
                        ": " + exception.Message);
                }
            }

            _services.Clear();
            _fixture.Dispose();
        }
    }

    /// <summary>
    /// Reusable invariant checks. Every assertion message carries the case description and the
    /// iteration context so a failure names exactly which invariant broke.
    /// </summary>
    internal static class AuditInvariants
    {
        private static readonly List<UINavigationEntry> HistoryScratch =
            new List<UINavigationEntry>(64);

        private static readonly List<UIWindow> ActiveWindowScratch =
            new List<UIWindow>(64);

        /// <summary>
        /// Asserts the steady-state invariants: session count, layer count, physical window
        /// objects and the navigation graph all agree with the service's active window count,
        /// and no session is stuck in the opening or closing phase.
        /// </summary>
        public static void AssertSettled(
            UIService service,
            UILayer layer,
            IUINavigationService navigation,
            AuditAssetProvider provider,
            int expectedActive,
            string context)
        {
            Assert.That(service.IsDisposed, Is.False, context + " | service must not be disposed");

            UIPerformanceStats stats = service.GetPerformanceStats();
            Assert.That(
                service.ActiveWindowCount,
                Is.EqualTo(expectedActive),
                context + " | (a) ActiveWindowCount");

            Assert.That(
                stats.SessionCount,
                Is.EqualTo(expectedActive),
                context + " | (b) session count must equal the active window count; stats=" + Describe(stats));

            Assert.That(
                stats.OpeningWindowCount,
                Is.EqualTo(0),
                context + " | no session may remain in the opening phase; stats=" + Describe(stats));

            Assert.That(
                stats.ClosingWindowCount,
                Is.EqualTo(0),
                context + " | no session may remain in the closing phase; stats=" + Describe(stats));

            Assert.That(
                layer.WindowCount,
                Is.EqualTo(expectedActive),
                context + " | (c) UILayer.WindowCount");

            Assert.That(
                AuditHarness.CountAttachedWindowObjects(layer),
                Is.EqualTo(expectedActive),
                context + " | (e) live window GameObjects under the layer");

            int copied = service.CopyActiveWindows(ActiveWindowScratch);
            Assert.That(
                copied,
                Is.EqualTo(expectedActive),
                context + " | CopyActiveWindows must report the expected active windows");

            if (navigation != null)
            {
                int nodes = navigation.CopyHistory(HistoryScratch);
                Assert.That(
                    nodes,
                    Is.EqualTo(expectedActive),
                    context + " | (f) navigation graph node count must equal the active window count");
            }

            if (provider != null)
            {
                AssertLeaseBounds(provider, expectedActive, context);
            }
        }

        /// <summary>
        /// Bounded lease check usable while sessions are still alive: a live session owns at most
        /// one configuration lease and at most one prefab lease, and a double dispose is never
        /// legal. Use <see cref="AssertLeaseAccounting"/> when every window is closed.
        /// </summary>
        public static void AssertLeaseBounds(
            AuditAssetProvider provider,
            int activeWindows,
            string context)
        {
            if (provider == null)
            {
                return;
            }

            if (provider.DoubleDisposeCount != 0)
            {
                Assert.Fail(
                    context + " | (d) a provider lease was disposed more than once; doubleDispose=" +
                    provider.DoubleDisposeCount);
            }

            int configurationOutstanding = provider.ConfigurationOutstanding;
            if (configurationOutstanding < 0 || configurationOutstanding > activeWindows)
            {
                Assert.Fail(
                    context + " | (d) outstanding configuration leases=" + configurationOutstanding +
                    " is outside [0," + activeWindows + "] (acquired=" +
                    provider.ConfigurationAcquireCount + ", disposed=" +
                    provider.ConfigurationDisposeCount + ")");
            }

            int prefabOutstanding = provider.PrefabOutstanding;
            if (prefabOutstanding < 0 || prefabOutstanding > activeWindows)
            {
                Assert.Fail(
                    context + " | (d) outstanding prefab leases=" + prefabOutstanding +
                    " is outside [0," + activeWindows + "] (acquired=" +
                    provider.PrefabAcquireCount + ", disposed=" + provider.PrefabDisposeCount + ")");
            }
        }

        /// <summary>
        /// Asserts that every provider lease acquired so far was disposed exactly once.
        /// </summary>
        public static void AssertLeaseAccounting(AuditAssetProvider provider, string context)
        {
            Assert.That(
                provider.DoubleDisposeCount,
                Is.EqualTo(0),
                context + " | (d) a provider lease was disposed more than once");

            Assert.That(
                provider.ConfigurationDisposeCount,
                Is.EqualTo(provider.ConfigurationAcquireCount),
                context + " | (d) configuration leases: acquired=" + provider.ConfigurationAcquireCount +
                " disposed=" + provider.ConfigurationDisposeCount);

            Assert.That(
                provider.PrefabDisposeCount,
                Is.EqualTo(provider.PrefabAcquireCount),
                context + " | (d) prefab leases: acquired=" + provider.PrefabAcquireCount +
                " disposed=" + provider.PrefabDisposeCount);
        }

        /// <summary>
        /// Self-consistency check for the fuzz walk: every ownership surface must agree with
        /// <see cref="UIService.ActiveWindowCount"/> and the lease ledger must balance.
        /// </summary>
        public static void CheckConsistent(
            UIService service,
            UILayer layer,
            IUINavigationService navigation,
            AuditAssetProvider provider,
            string context)
        {
            int active = service.ActiveWindowCount;
            UIPerformanceStats stats = service.GetPerformanceStats();

            if (stats.SessionCount != active)
            {
                Fail(context + " | session count " + stats.SessionCount +
                     " does not match ActiveWindowCount " + active + " | " + Describe(stats));
            }

            if (stats.OpeningWindowCount != 0 || stats.ClosingWindowCount != 0)
            {
                Fail(context + " | a session stayed in a transitional phase | " + Describe(stats));
            }

            if (active > stats.MaxWindowCapacity)
            {
                Fail(context + " | ActiveWindowCount " + active + " exceeds MaxWindowCapacity " +
                     stats.MaxWindowCapacity);
            }

            if (layer.WindowCount != active)
            {
                Fail(context + " | layer.WindowCount " + layer.WindowCount +
                     " does not match ActiveWindowCount " + active);
            }

            int physical = AuditHarness.CountAttachedWindowObjects(layer);
            if (physical != active)
            {
                Fail(context + " | " + physical +
                     " window GameObjects are parented under the layer but ActiveWindowCount is " + active);
            }

            int copied = service.CopyActiveWindows(ActiveWindowScratch);
            if (copied != active)
            {
                Fail(context + " | CopyActiveWindows reported " + copied +
                     " windows but ActiveWindowCount is " + active);
            }

            for (int i = 0; i < ActiveWindowScratch.Count; i++)
            {
                UIWindow window = ActiveWindowScratch[i];
                if (window == null)
                {
                    Fail(context + " | CopyActiveWindows returned a null entry at index " + i);
                }

                UIWindow byService;
                if (!service.TryGetWindow(window.WindowId, out byService) ||
                    !ReferenceEquals(byService, window))
                {
                    Fail(context + " | TryGetWindow(" + window.WindowId +
                         ") does not return the window reported by CopyActiveWindows");
                }

                UIWindow byLayer;
                if (!layer.TryGetWindow(window.WindowId, out byLayer) ||
                    !ReferenceEquals(byLayer, window))
                {
                    Fail(context + " | UILayer.TryGetWindow(" + window.WindowId +
                         ") disagrees with UIService for the same window");
                }

                if (window.State != UIWindowState.Open)
                {
                    Fail(context + " | active window '" + window.WindowId + "' is in state " +
                         window.State + " instead of Open");
                }
            }

            AssertLeaseBounds(provider, active, context);

            if (navigation != null)
            {
                int nodes = navigation.CopyHistory(HistoryScratch);
                if (nodes != active)
                {
                    var detail = new System.Text.StringBuilder(128);
                    detail.Append(context).Append(" | navigation nodes ").Append(nodes)
                        .Append(" do not match ActiveWindowCount ").Append(active)
                        .Append(" | nav=[");
                    for (int i = 0; i < HistoryScratch.Count; i++)
                    {
                        if (i > 0) { detail.Append(','); }
                        detail.Append(HistoryScratch[i].WindowId);
                    }

                    detail.Append("] active=[");
                    for (int i = 0; i < ActiveWindowScratch.Count; i++)
                    {
                        if (i > 0) { detail.Append(','); }
                        UIWindow activeWindow = ActiveWindowScratch[i];
                        detail.Append(activeWindow == null ? "<fake-null>" : activeWindow.WindowId);
                    }

                    detail.Append(']');
                    Fail(detail.ToString());
                }
            }
        }

        /// <summary>
        /// Allocation free variant of <see cref="AssertSettled"/> for hot soak loops: it performs
        /// the same comparisons but only builds a message when an invariant is violated. The
        /// messages are intentionally concise because this runs once per iteration.
        /// </summary>
        public static void CheckSettled(
            UIService service,
            UILayer layer,
            IUINavigationService navigation,
            AuditAssetProvider provider,
            int expectedActive,
            string context)
        {
            UIPerformanceStats stats = service.GetPerformanceStats();

            if (service.ActiveWindowCount != expectedActive)
            {
                Fail(context + " | (a) ActiveWindowCount=" + service.ActiveWindowCount +
                     " expected " + expectedActive);
            }

            if (stats.SessionCount != expectedActive)
            {
                Fail(context + " | (b) SessionCount=" + stats.SessionCount +
                     " expected " + expectedActive + " | " + Describe(stats));
            }

            if (stats.OpeningWindowCount != 0 || stats.ClosingWindowCount != 0)
            {
                Fail(context + " | a session stayed in a transitional phase | " + Describe(stats));
            }

            if (layer.WindowCount != expectedActive)
            {
                Fail(context + " | (c) layer.WindowCount=" + layer.WindowCount +
                     " expected " + expectedActive);
            }

            if (AuditHarness.CountAttachedWindowObjects(layer) != expectedActive)
            {
                Fail(context + " | (e) live window GameObjects under the layer do not match " +
                     expectedActive);
            }

            AssertLeaseBounds(provider, expectedActive, context);

            if (navigation != null)
            {
                int nodes = navigation.CopyHistory(HistoryScratch);
                if (nodes != expectedActive)
                {
                    Fail(context + " | (f) navigation nodes=" + nodes + " expected " + expectedActive);
                }
            }
        }

        /// <summary>
        /// True when every acquired lease was disposed exactly once. Used by probes that only
        /// need a boolean instead of a full assertion set.
        /// </summary>
        public static bool LeaseBalance(AuditAssetProvider provider)
        {
            return provider.DoubleDisposeCount == 0 &&
                   provider.ConfigurationDisposeCount == provider.ConfigurationAcquireCount &&
                   provider.PrefabDisposeCount == provider.PrefabAcquireCount;
        }

        /// <summary>Asserts that both window lookup paths agree for one id.</summary>
        public static UIWindow AssertWindowSurface(
            UIService service,
            UILayer layer,
            string windowId,
            string context)
        {
            UIWindow fromService;
            bool serviceFound = service.TryGetWindow(windowId, out fromService);
            UIWindow fromLayer;
            bool layerFound = layer.TryGetWindow(windowId, out fromLayer);

            Assert.That(
                serviceFound,
                Is.EqualTo(layerFound),
                context + " | (c) UIService.TryGetWindow and UILayer.TryGetWindow disagree for '" +
                windowId + "'");

            if (serviceFound)
            {
                Assert.That(
                    ReferenceEquals(fromService, fromLayer),
                    Is.True,
                    context + " | (c) both lookup paths must return the same window instance for '" +
                    windowId + "'");
            }

            return fromService;
        }

        public static string Describe(UIPerformanceStats stats)
        {
            return "sessions=" + stats.SessionCount +
                   ",opening=" + stats.OpeningWindowCount +
                   ",open=" + stats.OpenWindowCount +
                   ",closing=" + stats.ClosingWindowCount +
                   ",sceneBound=" + stats.SceneBoundWindowCount +
                   ",binders=" + stats.BinderCount +
                   ",isolated=" + stats.IsolatedWindowCanvasCount +
                   ",layers=" + stats.LayerCount +
                   ",maxWindows=" + stats.MaxWindowCapacity;
        }

        private static void Fail(string message)
        {
            Assert.Fail(message);
        }
    }
}
