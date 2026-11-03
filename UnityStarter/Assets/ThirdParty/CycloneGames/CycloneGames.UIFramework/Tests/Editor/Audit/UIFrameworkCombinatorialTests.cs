using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CycloneGames.UIFramework.Tests.Editor.Audit
{
    /// <summary>
    /// Pairwise combination of the open/close pipeline: each tuple is a separate test case and
    /// one sweep test runs the whole matrix. EditMode has no PlayerLoop tick, so providers and
    /// transitions complete synchronously and the per-frame instantiate budget is not exercised.
    /// </summary>
    [Category("UIFrameworkAudit")]
    public sealed class UIFrameworkCombinatorialTests
    {
        private const int CaseTimeoutMilliseconds = 120000;
        private const int SweepTimeoutMilliseconds = 600000;

        // ------------------------------------------------------------------ generator

        [Test]
        public void PairwiseGenerator_CoversEveryTwoDimensionalCombination()
        {
            List<AuditTuple> cases = AuditPairwise.GetCases();

            string missing;
            bool complete = AuditPairwise.VerifyCoverage(cases, out missing);

            Assert.That(
                complete,
                Is.True,
                "Pairwise coverage is incomplete; the first missing combination is " + missing +
                ". Every two-dimensional value pair must appear at least once (" +
                AuditPairwise.PairCount() + " required pairs).");
        }

        [Test]
        public void PairwiseGenerator_MeetsMinimumCaseCount()
        {
            List<AuditTuple> cases = AuditPairwise.GetCases();

            Assert.That(
                cases.Count,
                Is.GreaterThanOrEqualTo(AuditPairwise.MinimumRequiredCases),
                "The audit matrix must contain at least " + AuditPairwise.MinimumRequiredCases +
                " tuples; generated " + cases.Count + ".");
        }

        [Test]
        public void PairwiseGenerator_IsDeterministic()
        {
            List<AuditTuple> first = AuditPairwise.Rebuild();
            List<AuditTuple> second = AuditPairwise.Rebuild();

            Assert.That(
                second.Count,
                Is.EqualTo(first.Count),
                "Two generator runs produced a different case count; the generator is not deterministic.");

            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(
                    second[i].Describe(),
                    Is.EqualTo(first[i].Describe()),
                    "Generator output differs at index " + i +
                    "; the fixed seed must reproduce the same tuple order.");
            }
        }

        // ------------------------------------------------------------------ matrix

        [TestCaseSource(nameof(MatrixCaseSource))]
        [Timeout(CaseTimeoutMilliseconds)]
        public async Task MatrixCase_HoldsInvariantsForEveryDimensionCombination(
            int source,
            int binders,
            int transition,
            int options,
            int navigation,
            int canvas,
            int cull,
            int capacity)
        {
            var tuple = new AuditTuple(
                source, binders, transition, options, navigation, canvas, cull, capacity);

            await ExecuteCaseAsync(tuple);
        }

        [Test]
        [Timeout(SweepTimeoutMilliseconds)]
        public async Task Matrix_AllPairwiseCases_InOnePass()
        {
            List<AuditTuple> cases = AuditPairwise.GetCases();
            List<string> failures = new List<string>(8);

            for (int i = 0; i < cases.Count; i++)
            {
                AuditTuple tuple = cases[i];
                try
                {
                    await ExecuteCaseAsync(tuple);
                }
                catch (Exception exception)
                {
                    if (failures.Count < 12)
                    {
                        failures.Add(
                            "case[" + i + "] " + tuple.Describe() + " -> " +
                            exception.GetType().Name + ": " + exception.Message);
                    }
                    else
                    {
                        failures.Add("case[" + i + "] " + tuple.Describe() + " -> (further failures suppressed)");
                    }
                }
            }

            if (failures.Count > 0)
            {
                Assert.Fail(
                    failures.Count + " of " + cases.Count + " matrix cases violated the audit invariants:\n" +
                    string.Join("\n", failures.ToArray()));
            }
        }

        private static IEnumerable<TestCaseData> MatrixCaseSource()
        {
            List<AuditTuple> cases = AuditPairwise.GetCases();
            List<TestCaseData> data = new List<TestCaseData>(cases.Count);
            for (int i = 0; i < cases.Count; i++)
            {
                AuditTuple tuple = cases[i];
                var testCase = new TestCaseData(
                    tuple.Source,
                    tuple.Binders,
                    tuple.Transition,
                    tuple.Options,
                    tuple.Navigation,
                    tuple.Canvas,
                    tuple.Cull,
                    tuple.Capacity);
                testCase.SetName("Matrix_" + i.ToString("D2") + "_" + ShortTag(tuple));
                testCase.SetDescription(tuple.Describe());
                data.Add(testCase);
            }

            return data;
        }

        private static string ShortTag(AuditTuple tuple)
        {
            string source = "src?";
            switch (tuple.Source)
            {
                case AuditDims.SourcePrefabReference: source = "prefabRef"; break;
                case AuditDims.SourceAssetReference: source = "assetRef"; break;
                case AuditDims.SourcePathLocation: source = "pathLoc"; break;
            }

            string binders = "bind?";
            switch (tuple.Binders)
            {
                case AuditDims.BindersNone: binders = "noBinder"; break;
                case AuditDims.BindersSingle: binders = "oneBinder"; break;
                case AuditDims.BindersThreeWithFailingSecond: binders = "threeBindersFail2"; break;
            }

            string transition = "trans?";
            switch (tuple.Transition)
            {
                case AuditDims.TransitionNone: transition = "noTrans"; break;
                case AuditDims.TransitionSyncSuccess: transition = "syncTrans"; break;
                case AuditDims.TransitionThrowOnOpen: transition = "throwTrans"; break;
                case AuditDims.TransitionCancelOnOpen: transition = "cancelTrans"; break;
            }

            string options = "opt?";
            switch (tuple.Options)
            {
                case AuditDims.OptionsPlain: options = "plain"; break;
                case AuditDims.OptionsContext: options = "ctx"; break;
                case AuditDims.OptionsOpenerWithContext: options = "opener"; break;
                case AuditDims.OptionsOpenerSuppressed: options = "suppressed"; break;
                case AuditDims.OptionsSceneBoundOverrideTrue: options = "sboTrue"; break;
                case AuditDims.OptionsSceneBoundOverrideFalse: options = "sboFalse"; break;
            }

            string navigation = tuple.Navigation == AuditDims.NavigationEnabled ? "navOn" : "navOff";
            string canvas = tuple.Canvas == AuditDims.CanvasIsolated ? "isolated" : "inherit";
            string cull = tuple.Cull == AuditDims.CullEnabled ? "cull" : "noCull";
            string capacity = tuple.Capacity == AuditDims.CapacityBoundarySingleSlot ? "cap1" : "cap8";

            return source + "_" + binders + "_" + transition + "_" + options + "_" +
                   navigation + "_" + canvas + "_" + cull + "_" + capacity;
        }

        // ------------------------------------------------------------------ case body

        private static async Task ExecuteCaseAsync(AuditTuple tuple)
        {
            string context = tuple.Describe();
            using (var harness = new AuditHarness())
            {
                UILayer layer = harness.Layer;
                Assert.That(
                    layer.WindowCount,
                    Is.EqualTo(0),
                    context + " | the audit layer must start empty");

                UINavigationService navigation = null;
                if (tuple.Navigation == AuditDims.NavigationEnabled)
                {
                    navigation = harness.CreateNavigation(8);
                }

                AuditAssetProvider provider = harness.CreateProvider();

                UIWindowConfiguration configuration;
                if (tuple.Source == AuditDims.SourcePrefabReference)
                {
                    configuration = harness.CreateDirect(AuditDims.TargetWindowId, 10, false);
                }
                else
                {
                    configuration = harness.CreateProviderConfiguration(
                        AuditDims.TargetWindowId, tuple.Source, 10);
                }

                AuditHarness.ApplyCanvasPolicy(configuration, tuple.Canvas);
                AuditHarness.ApplyCullWhenHidden(configuration, tuple.Cull);
                provider.Map(
                    AuditDims.TargetWindowId,
                    configuration,
                    harness.CreatePrefab(AuditDims.TargetWindowId));

                // The opener is always a direct PrefabReference window so D2 faults can be
                // scoped to the target id and cannot poison the pre-opened opener.
                UIWindowConfiguration openerConfiguration =
                    harness.CreateDirect(AuditDims.OpenerWindowId, 5, false);
                provider.Map(
                    AuditDims.OpenerWindowId,
                    openerConfiguration,
                    harness.CreatePrefab(AuditDims.OpenerWindowId));

                List<AuditBinder> binders = BuildBinders(tuple.Binders);
                UIServiceOptions options = AuditHarness.CreateOptions(tuple.Capacity, navigation);
                UIService service = harness.CreateService(provider, options, binders);

                int expectedActive = 0;
                if (tuple.RequiresPreOpenedOpener())
                {
                    UIOpenOptions openerOptions =
                        new UIOpenOptions(null, null, null, default, null, true);
                    await service.OpenAsync(openerConfiguration, openerOptions);
                    expectedActive = 1;
                    AuditInvariants.AssertSettled(
                        service, layer, navigation, provider, 1, context + " | opener pre-open");
                }

                // Lease accounting restarts here so the assertions describe this case only.
                provider.ResetCounters();

                AuditTransitionDriver driver = BuildDriver(tuple.Transition);
                object payload = new object();
                UIOpenOptions openOptions = AuditOptions.Create(tuple.Options, payload, driver);

                bool taskProduced = false;
                UniTask<UIWindow> openTask = default(UniTask<UIWindow>);
                Exception synchronousError = null;
                try
                {
                    openTask = tuple.Source == AuditDims.SourcePrefabReference
                        ? service.OpenAsync(configuration, openOptions)
                        : service.OpenAsync(AuditDims.TargetWindowId, openOptions);
                    taskProduced = true;
                }
                catch (Exception exception)
                {
                    synchronousError = exception;
                }

                UIWindow opened = null;
                Exception awaitedError = null;
                if (taskProduced)
                {
                    try
                    {
                        opened = await openTask;
                    }
                    catch (Exception exception)
                    {
                        awaitedError = exception;
                    }
                }

                Exception observed = synchronousError != null ? synchronousError : awaitedError;
                AuditExpectedOutcome expected = AuditPredictor.Predict(tuple);
                AssertOutcome(tuple, expected, observed, opened, service, context);

                if (expected == AuditExpectedOutcome.Success)
                {
                    expectedActive++;
                }

                AuditInvariants.AssertSettled(
                    service,
                    layer,
                    navigation,
                    provider,
                    expectedActive,
                    context + " | after the target open attempt (" + expected + ")");

                // Asset acquisition accounting. A capacity rejection happens before any asset
                // work, so provider counters must stay untouched in that path.
                if (expected == AuditExpectedOutcome.CapacityRejected ||
                    tuple.Source == AuditDims.SourcePrefabReference)
                {
                    Assert.That(
                        provider.ConfigurationAcquireCount,
                        Is.EqualTo(0),
                        context + " | no configuration may be acquired for a direct or capacity-rejected open");
                    Assert.That(
                        provider.PrefabAcquireCount,
                        Is.EqualTo(0),
                        context + " | no prefab may be acquired for a direct or capacity-rejected open");
                }
                else
                {
                    Assert.That(
                        provider.ConfigurationAcquireCount,
                        Is.EqualTo(1),
                        context + " | a provider-sourced open must acquire exactly one configuration lease");
                    Assert.That(
                        provider.PrefabAcquireCount,
                        Is.EqualTo(1),
                        context + " | a provider-sourced open must acquire exactly one prefab lease");
                }

                if (expected == AuditExpectedOutcome.Success)
                {
                    AssertSuccessSurface(tuple, service, layer, opened, driver, context);

                    bool closed = await service.CloseAsync(
                        AuditDims.TargetWindowId,
                        ChildClosePolicy.Reparent);
                    Assert.That(
                        closed,
                        Is.True,
                        context + " | closing the committed target must report true");
                    expectedActive--;

                    if (binders.Count > 0)
                    {
                        Assert.That(
                            binders[0].FinishedCloseStateValue,
                            Is.EqualTo((int)UIWindowState.Closed),
                            context + " | (g) bindings must observe the window as Closed when " +
                            "OnFinishedClose is dispatched");
                    }

                    AuditInvariants.AssertSettled(
                        service,
                        layer,
                        navigation,
                        provider,
                        expectedActive,
                        context + " | after closing the target");
                }

                if (expectedActive > 0)
                {
                    bool openerClosed = await service.CloseAsync(
                        AuditDims.OpenerWindowId,
                        ChildClosePolicy.Reparent);
                    Assert.That(
                        openerClosed,
                        Is.True,
                        context + " | closing the opener must report true");
                    expectedActive = 0;
                    AuditInvariants.AssertSettled(
                        service,
                        layer,
                        navigation,
                        provider,
                        0,
                        context + " | after closing the opener");
                }

                service.Dispose();

                Assert.That(
                    service.IsDisposed,
                    Is.True,
                    context + " | Dispose must publish IsDisposed");
                Assert.That(
                    layer.WindowCount,
                    Is.EqualTo(0),
                    context + " | Dispose must detach every window from the layer");
                Assert.That(
                    AuditHarness.CountAttachedWindowObjects(layer),
                    Is.EqualTo(0),
                    context + " | (e) every window GameObject must be destroyed after Dispose");

                for (int i = 0; i < binders.Count; i++)
                {
                    Assert.That(
                        binders[i].DisposeCount + binders[i].BindFaultCount,
                        Is.EqualTo(binders[i].BindCount),
                        context + " | every created binding must be disposed exactly once for binder '" +
                        binders[i].Name + "' (bound=" + binders[i].BindCount +
                        " disposed=" + binders[i].DisposeCount +
                        " bindFaults=" + binders[i].BindFaultCount + ")");
                }

                AuditInvariants.AssertLeaseAccounting(provider, context + " | final lease accounting");

                if (navigation != null)
                {
                    var history = new List<UINavigationEntry>(8);
                    Assert.That(
                        navigation.CopyHistory(history),
                        Is.EqualTo(0),
                        context + " | (f) the navigation graph must be empty after every window closed");
                }
            }
        }

        private static void AssertOutcome(
            AuditTuple tuple,
            AuditExpectedOutcome expected,
            Exception observed,
            UIWindow opened,
            UIService service,
            string context)
        {
            switch (expected)
            {
                case AuditExpectedOutcome.Success:
                    Assert.That(
                        observed,
                        Is.Null,
                        context + " | expected a successful open but observed " + DescribeError(observed));
                    Assert.That(
                        opened,
                        Is.Not.Null,
                        context + " | a successful open must yield a window instance");
                    Assert.That(
                        opened.State,
                        Is.EqualTo(UIWindowState.Open),
                        context + " | (g) a committed window must be in the Open state");
                    break;

                case AuditExpectedOutcome.CapacityRejected:
                    Assert.That(
                        observed,
                        Is.Not.Null,
                        context + " | opening above MaxActiveWindows must fail");
                    Assert.That(
                        observed,
                        Is.InstanceOf<InvalidOperationException>(),
                        context + " | a capacity overflow must throw InvalidOperationException, observed " +
                        DescribeError(observed));
                    Assert.That(
                        observed.Message.Contains("capacity"),
                        Is.True,
                        context + " | the capacity rejection must name the capacity limit, message='" +
                        observed.Message + "'");
                    Assert.That(
                        service.GetPerformanceStats().MaxWindowCapacity,
                        Is.EqualTo(1),
                        context + " | the boundary profile must configure MaxActiveWindows=1");
                    break;

                case AuditExpectedOutcome.BinderRejected:
                    Assert.That(
                        observed,
                        Is.Not.Null,
                        context + " | a throwing binder must fail the open");
                    Assert.That(
                        observed,
                        Is.InstanceOf<InvalidOperationException>(),
                        context + " | a binder failure must surface as InvalidOperationException, observed " +
                        DescribeError(observed));
                    Assert.That(
                        observed.Message.Contains("bind failed"),
                        Is.True,
                        context + " | the binder failure must be propagated, message='" +
                        observed.Message + "'");
                    break;

                case AuditExpectedOutcome.TransitionThrew:
                    Assert.That(
                        observed,
                        Is.Not.Null,
                        context + " | a throwing open transition must fail the open");
                    Assert.That(
                        observed,
                        Is.InstanceOf<InvalidOperationException>(),
                        context + " | a transition fault must surface as InvalidOperationException, observed " +
                        DescribeError(observed));
                    Assert.That(
                        observed.Message.Contains("transition"),
                        Is.True,
                        context + " | the transition fault must be propagated, message='" +
                        observed.Message + "'");
                    break;

                case AuditExpectedOutcome.TransitionCanceled:
                    Assert.That(
                        observed,
                        Is.Not.Null,
                        context + " | a canceled open transition must fail the open");
                    Assert.That(
                        observed,
                        Is.InstanceOf<OperationCanceledException>(),
                        context + " | a canceled transition must surface as OperationCanceledException, observed " +
                        DescribeError(observed));
                    break;
            }
        }

        private static void AssertSuccessSurface(
            AuditTuple tuple,
            UIService service,
            UILayer layer,
            UIWindow opened,
            AuditTransitionDriver driver,
            string context)
        {
            Assert.That(
                opened.WindowId,
                Is.EqualTo(AuditDims.TargetWindowId),
                context + " | the committed window must carry the requested id");

            UIWindow fromService = AuditInvariants.AssertWindowSurface(
                service, layer, AuditDims.TargetWindowId, context);
            Assert.That(
                ReferenceEquals(fromService, opened),
                Is.True,
                context + " | (c) the lookup surface must return the committed instance");

            bool isolated = tuple.Canvas == AuditDims.CanvasIsolated;
            Assert.That(
                opened.GetComponent<Canvas>() != null,
                Is.EqualTo(isolated),
                context + " | D6 " + AuditDims.CanvasName(tuple.Canvas) +
                " must add a Canvas only for the isolated policy");
            Assert.That(
                service.GetPerformanceStats().IsolatedWindowCanvasCount,
                Is.EqualTo(isolated ? 1 : 0),
                context + " | D6 isolated canvas accounting");

            bool sceneBound = tuple.Options == AuditDims.OptionsSceneBoundOverrideTrue;
            Assert.That(
                opened.IsSceneBound,
                Is.EqualTo(sceneBound),
                context + " | D4 SceneBoundOverride must decide the scene-bound flag");
            int activeSceneHandle = SceneManager.GetActiveScene().handle;
            Assert.That(
                opened.BoundSceneHandle,
                Is.EqualTo(sceneBound ? activeSceneHandle : -1),
                context + " | D4 scene-bound windows must capture the requesting scene handle");
            Assert.That(
                service.GetPerformanceStats().SceneBoundWindowCount,
                Is.EqualTo(sceneBound ? 1 : 0),
                context + " | D4 scene-bound accounting");

            bool transitionSuppressed = tuple.Options == AuditDims.OptionsOpenerSuppressed;
            if (driver != null)
            {
                Assert.That(
                    driver.OpenCallCount,
                    Is.EqualTo(transitionSuppressed ? 0 : 1),
                    context + " | D3/D4 the open transition driver must run exactly once unless " +
                    "SuppressWindowTransition is set");
            }

            bool cull = tuple.Cull == AuditDims.CullEnabled;
            opened.SetVisible(false);
            Assert.That(
                opened.gameObject.activeSelf,
                Is.EqualTo(!cull),
                context + " | D7 CullWhenHidden=false must keep the GameObject active while hidden");
            opened.SetVisible(true);
            Assert.That(
                opened.gameObject.activeSelf,
                Is.True,
                context + " | D7 showing the window again must reactivate the GameObject");
        }

        private static List<AuditBinder> BuildBinders(int shape)
        {
            var binders = new List<AuditBinder>(3);
            if (shape == AuditDims.BindersNone)
            {
                return binders;
            }

            binders.Add(new AuditBinder("AuditBinderB0"));
            if (shape == AuditDims.BindersSingle)
            {
                return binders;
            }

            var failing = new AuditBinder("AuditBinderB1");
            failing.ThrowOnBind = true;
            failing.ThrowForWindowId = AuditDims.TargetWindowId;
            binders.Add(failing);
            binders.Add(new AuditBinder("AuditBinderB2"));
            return binders;
        }

        private static AuditTransitionDriver BuildDriver(int transition)
        {
            switch (transition)
            {
                case AuditDims.TransitionSyncSuccess:
                    return new AuditTransitionDriver();
                case AuditDims.TransitionThrowOnOpen:
                    var throwing = new AuditTransitionDriver();
                    throwing.OpenThrowMessage = "Audit open transition failure.";
                    return throwing;
                case AuditDims.TransitionCancelOnOpen:
                    var canceling = new AuditTransitionDriver();
                    canceling.CancelOpen = true;
                    return canceling;
                default:
                    return null;
            }
        }

        private static string DescribeError(Exception exception)
        {
            return exception == null
                ? "<no exception>"
                : exception.GetType().Name + ": " + exception.Message;
        }
    }

    /// <summary>
    /// Semantic boundary cases pinned by explicit tests rather than generated combinations:
    /// concurrency, cancellation, navigation policies and shutdown.
    /// </summary>
    [Category("UIFrameworkAudit")]
    public sealed class UIFrameworkSemanticBoundaryTests
    {
        private const string ClosedWindowId = "SemanticWindow";

        // ------------------------------------------------------------------ duplicate open

        [Test]
        public async Task DuplicateOpen_IdenticalConfigurationAndOptions_SharesOneInstance()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect(ClosedWindowId, 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);

                UIWindow first = await service.OpenAsync(configuration);
                UIWindow second = await service.OpenAsync(configuration);

                Assert.That(
                    ReferenceEquals(first, second),
                    Is.True,
                    "A duplicate open with identical configuration and identical options must join " +
                    "the committed session instead of instantiating a second window.");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(1), "only one window may be active");
                Assert.That(
                    AuditHarness.CountAttachedWindowObjects(harness.Layer),
                    Is.EqualTo(1),
                    "exactly one window GameObject may exist under the layer");

                await service.CloseAsync(ClosedWindowId);
                AuditInvariants.AssertSettled(
                    service, harness.Layer, null, provider, 0, "duplicate open cleanup");
            }
        }

        [Test]
        public async Task DuplicateOpen_DifferentConfigurationInstance_Throws()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration firstConfiguration = harness.CreateDirect(ClosedWindowId, 0, false);
                UIWindowConfiguration secondConfiguration = harness.CreateDirect(ClosedWindowId, 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);

                await service.OpenAsync(firstConfiguration);

                Exception observed = null;
                try
                {
                    service.OpenAsync(secondConfiguration);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "A duplicate open from a different configuration instance must throw " +
                    "InvalidOperationException, observed " + DescribeError(observed));
                Assert.That(
                    observed.Message.Contains("different configuration"),
                    Is.True,
                    "the rejection must name the configuration mismatch, message='" + observed.Message + "'");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(1), "the first session must survive");

                Assert.That(
                    await service.CloseAsync(ClosedWindowId),
                    Is.True,
                    "the surviving session must still close");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(0), "the closed session must release its slot");
            }
        }

        [Test]
        public async Task DuplicateOpen_DifferentContextReference_Throws()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect(ClosedWindowId, 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);

                await service.OpenAsync(configuration, new UIOpenOptions(context: new object()));

                Exception observed = null;
                try
                {
                    service.OpenAsync(configuration, new UIOpenOptions(context: new object()));
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "A duplicate open whose Context reference differs must throw, observed " +
                    DescribeError(observed));
                Assert.That(
                    observed.Message.Contains("different options"),
                    Is.True,
                    "the rejection must name the option mismatch, message='" + observed.Message + "'");

                await service.CloseAsync(ClosedWindowId);
            }
        }

        // ------------------------------------------------------------------ capacity

        [Test]
        public async Task Capacity_BoundaryProfile_RejectsThenAcceptsAfterClose()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration first = harness.CreateDirect("CapacityFirst", 0, false);
                UIWindowConfiguration second = harness.CreateDirect("CapacitySecond", 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityBoundarySingleSlot, null), null);

                await service.OpenAsync(first);

                Exception observed = null;
                try
                {
                    service.OpenAsync(second);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "opening past MaxActiveWindows=1 must throw, observed " + DescribeError(observed));
                Assert.That(
                    service.ActiveWindowCount,
                    Is.EqualTo(1),
                    "a rejected open must not increase the active window count");

                await service.CloseAsync("CapacityFirst");
                UIWindow reopened = await service.OpenAsync(second);
                Assert.That(
                    reopened.WindowId,
                    Is.EqualTo("CapacitySecond"),
                    "a closed session must release its capacity slot");

                await service.CloseAsync("CapacitySecond");
                AuditInvariants.AssertSettled(service, harness.Layer, null, provider, 0, "capacity boundary");
            }
        }

        // ------------------------------------------------------------------ close semantics

        [Test]
        public async Task Close_UnknownWindowId_ReturnsFalse()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);

                Assert.That(
                    await service.CloseAsync("NeverOpened"),
                    Is.False,
                    "closing an unknown window id must report false");
                Assert.That(
                    await service.CloseAsync(null),
                    Is.False,
                    "closing a null window id must report false");
                Assert.That(
                    await service.CloseAsync(string.Empty),
                    Is.False,
                    "closing an empty window id must report false");
            }
        }

        [Test]
        public async Task Close_DuringCloseInFlight_IsIdempotentAndCleansUpOnce()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect(ClosedWindowId, 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                var driver = new AuditTransitionDriver();
                driver.CloseGate = new UniTaskCompletionSource();
                UIServiceOptions options = AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null);
                options.DefaultTransitionDriver = driver;
                var binder = new AuditBinder("CloseProbe");
                UIService service = harness.CreateService(provider, options, new[] { binder });

                await service.OpenAsync(configuration);

                UniTask<bool> firstClose = service.CloseAsync(ClosedWindowId);
                Assert.That(
                    firstClose.Status,
                    Is.EqualTo(UniTaskStatus.Pending),
                    "the gated close transition must keep the close in flight");

                UniTask<bool> secondClose = service.CloseAsync(ClosedWindowId);
                Assert.That(
                    secondClose.Status,
                    Is.EqualTo(UniTaskStatus.Pending),
                    "a close requested while another close is in flight must join it, not start a second cleanup");

                driver.CloseGate.TrySetResult();

                Assert.That(await firstClose, Is.True, "the first close must complete successfully");
                Assert.That(await secondClose, Is.True, "the joined close must complete successfully");
                Assert.That(
                    binder.DisposeCount,
                    Is.EqualTo(1),
                    "exactly one binding dispose is allowed; a double cleanup would dispose twice");
                Assert.That(driver.CloseCallCount, Is.EqualTo(1), "the close transition must run exactly once");
                AuditInvariants.AssertSettled(service, harness.Layer, null, provider, 0, "idempotent close");
            }
        }

        [Test]
        public async Task Open_WhileCloseIsInFlight_Throws()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect(ClosedWindowId, 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                var driver = new AuditTransitionDriver();
                driver.CloseGate = new UniTaskCompletionSource();
                UIServiceOptions options = AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null);
                options.DefaultTransitionDriver = driver;
                UIService service = harness.CreateService(provider, options, null);

                await service.OpenAsync(configuration);
                UniTask<bool> close = service.CloseAsync(ClosedWindowId);

                Exception observed = null;
                try
                {
                    service.OpenAsync(configuration);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "opening a window that is still closing must throw, observed " + DescribeError(observed));
                Assert.That(
                    observed.Message.Contains("closing"),
                    Is.True,
                    "the rejection must name the closing state, message='" + observed.Message + "'");

                driver.CloseGate.TrySetResult();
                Assert.That(await close, Is.True, "the original close must still complete");
                AuditInvariants.AssertSettled(service, harness.Layer, null, provider, 0, "open during close");
            }
        }

        [Test]
        public async Task Close_DuringOpenInFlight_WaitsForConvergenceWithoutDoubleCleanup()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateProviderConfiguration(
                    ClosedWindowId, AuditDims.SourceAssetReference, 0);
                AuditAssetProvider provider = harness.CreateProvider();
                provider.Map(ClosedWindowId, configuration, harness.CreatePrefab(ClosedWindowId));
                provider.GateConfigurationAcquisition = true;
                provider.IgnoreCancellation = true;
                provider.ConfigurationGate =
                    new UniTaskCompletionSource<IUIAssetLease<UIWindowConfiguration>>();

                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);

                UniTask<UIWindow> open = service.OpenAsync(ClosedWindowId);
                Assert.That(
                    open.Status,
                    Is.EqualTo(UniTaskStatus.Pending),
                    "the gated provider must hold the open in flight");
                Assert.That(
                    service.GetPerformanceStats().OpeningWindowCount,
                    Is.EqualTo(1),
                    "the in-flight session must be reported as opening");

                UniTask<bool> close = service.CloseAsync(ClosedWindowId);
                Assert.That(
                    close.Status,
                    Is.EqualTo(UniTaskStatus.Pending),
                    "the close must wait for the in-flight open to converge");

                var lateLease = new AuditLease<UIWindowConfiguration>(configuration, provider, true);
                provider.ConfigurationGate.TrySetResult(lateLease);

                Assert.That(await close, Is.True, "the close must complete once the open converges");

                Exception openError = null;
                try
                {
                    await open;
                }
                catch (Exception exception)
                {
                    openError = exception;
                }

                Assert.That(
                    openError,
                    Is.InstanceOf<OperationCanceledException>(),
                    "the superseded open must be canceled, observed " + DescribeError(openError));
                Assert.That(
                    lateLease.DisposeCount,
                    Is.EqualTo(1),
                    "the late provider lease must be released exactly once; a double cleanup would " +
                    "dispose it twice");
                AuditInvariants.AssertSettled(
                    service, harness.Layer, null, provider, 0, "close during open");
            }
        }

        // ------------------------------------------------------------------ binder failures

        [Test]
        public async Task Binder_ThrowOnBind_RollsBackOpenWithoutLeaking()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateProviderConfiguration(
                    ClosedWindowId, AuditDims.SourceAssetReference, 0);
                AuditAssetProvider provider = harness.CreateProvider();
                provider.Map(ClosedWindowId, configuration, harness.CreatePrefab(ClosedWindowId));
                var failing = new AuditBinder("BindFailure");
                failing.ThrowOnBind = true;
                UIService service = harness.CreateService(
                    provider,
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    new IUIWindowBinder[] { failing });

                Exception observed = null;
                try
                {
                    await service.OpenAsync(ClosedWindowId);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "a throwing binder must fail the open, observed " + DescribeError(observed));
                Assert.That(observed.Message.Contains("bind failed"), Is.True, "the binder fault must propagate");
                Assert.That(failing.BindCount, Is.EqualTo(1), "the failing binder must be invoked once");
                Assert.That(failing.DisposeCount, Is.EqualTo(0), "no binding was created, so none can be disposed");
                AuditInvariants.AssertSettled(service, harness.Layer, null, provider, 0, "binder bind failure");
                Assert.That(
                    provider.ConfigurationDisposeCount,
                    Is.EqualTo(1),
                    "the configuration lease acquired before the failure must be released");
            }
        }

        [Test]
        public async Task Binder_ThrowOnStartOpen_RollsBackOpenWithoutLeaking()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateProviderConfiguration(
                    ClosedWindowId, AuditDims.SourceAssetReference, 0);
                AuditAssetProvider provider = harness.CreateProvider();
                provider.Map(ClosedWindowId, configuration, harness.CreatePrefab(ClosedWindowId));
                var failing = new AuditBinder("StartOpenFailure");
                failing.ThrowOnStage = (int)WindowStateCallbackType.OnStartOpen;
                UIService service = harness.CreateService(
                    provider,
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    new IUIWindowBinder[] { failing });

                Exception observed = null;
                try
                {
                    await service.OpenAsync(ClosedWindowId);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "an OnStartOpen failure must fail the open, observed " + DescribeError(observed));
                Assert.That(
                    failing.DisposeCount,
                    Is.EqualTo(1),
                    "the binding created before the OnStartOpen failure must be disposed exactly once");
                AuditInvariants.AssertSettled(service, harness.Layer, null, provider, 0, "OnStartOpen failure");
            }
        }

        [Test]
        public async Task Binder_ThrowOnFinishedOpen_RollsBackCommittedOpen()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateProviderConfiguration(
                    ClosedWindowId, AuditDims.SourceAssetReference, 0);
                AuditAssetProvider provider = harness.CreateProvider();
                provider.Map(ClosedWindowId, configuration, harness.CreatePrefab(ClosedWindowId));
                var failing = new AuditBinder("FinishedOpenFailure");
                failing.ThrowOnStage = (int)WindowStateCallbackType.OnFinishedOpen;
                UIService service = harness.CreateService(
                    provider,
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    new IUIWindowBinder[] { failing });

                Exception observed = null;
                try
                {
                    await service.OpenAsync(ClosedWindowId);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "an OnFinishedOpen failure must roll the committed open back, observed " +
                    DescribeError(observed));
                Assert.That(
                    service.ActiveWindowCount,
                    Is.EqualTo(0),
                    "the window must not stay committed after an OnFinishedOpen failure");
                Assert.That(
                    failing.DisposeCount,
                    Is.EqualTo(1),
                    "the binding must be disposed exactly once during rollback");
                AuditInvariants.AssertSettled(service, harness.Layer, null, provider, 0, "OnFinishedOpen failure");
            }
        }

        [Test]
        public async Task Binder_ThrowOnStartClose_CloseStillCompletesCleanupAndReportsFailure()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateProviderConfiguration(
                    ClosedWindowId, AuditDims.SourceAssetReference, 0);
                AuditAssetProvider provider = harness.CreateProvider();
                provider.Map(ClosedWindowId, configuration, harness.CreatePrefab(ClosedWindowId));
                var failing = new AuditBinder("StartCloseFailure");
                failing.ThrowOnStage = (int)WindowStateCallbackType.OnStartClose;
                UIService service = harness.CreateService(
                    provider,
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    new IUIWindowBinder[] { failing });

                await service.OpenAsync(ClosedWindowId);

                Exception observed = null;
                try
                {
                    await service.CloseAsync(ClosedWindowId);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "a single close-stage binder failure is reported unwrapped, observed " +
                    DescribeError(observed));
                Assert.That(
                    service.ActiveWindowCount,
                    Is.EqualTo(0),
                    "continueOnFailure=true requires the close to complete cleanup despite the fault");
                Assert.That(
                    provider.ConfigurationDisposeCount,
                    Is.EqualTo(1),
                    "cleanup must still release the configuration lease exactly once");
                Assert.That(
                    provider.PrefabDisposeCount,
                    Is.EqualTo(1),
                    "cleanup must still release the prefab lease exactly once");
                AuditInvariants.AssertSettled(service, harness.Layer, null, provider, 0, "OnStartClose failure");
            }
        }

        [Test]
        public async Task Binder_ThrowOnBothCloseStages_ReportsAggregateException()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect(ClosedWindowId, 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                var startFailure = new AuditBinder("StartCloseFailure");
                startFailure.ThrowOnStage = (int)WindowStateCallbackType.OnStartClose;
                var finishedFailure = new AuditBinder("FinishedCloseFailure");
                finishedFailure.ThrowOnStage = (int)WindowStateCallbackType.OnFinishedClose;
                UIService service = harness.CreateService(
                    provider,
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    new IUIWindowBinder[] { startFailure, finishedFailure });

                await service.OpenAsync(configuration);

                Exception observed = null;
                try
                {
                    await service.CloseAsync(ClosedWindowId);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<AggregateException>(),
                    "two close-stage failures must be aggregated, observed " + DescribeError(observed));
                var aggregate = (AggregateException)observed;
                Assert.That(
                    aggregate.InnerExceptions.Count,
                    Is.EqualTo(2),
                    "both close-stage failures must be preserved in the aggregate");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(0), "cleanup must complete despite both faults");
                AuditInvariants.AssertSettled(service, harness.Layer, null, provider, 0, "both close faults");
            }
        }

        // ------------------------------------------------------------------ re-entrancy

        [Test]
        public async Task ReentrantClose_FromBinderStartClose_IsRejectedSynchronously()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect(ClosedWindowId, 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                var binder = new AuditBinder("SelfCloseProbe");
                binder.ThrowForWindowId = ClosedWindowId;
                binder.SelfCloseOnStage = (int)WindowStateCallbackType.OnStartClose;
                UIService service = harness.CreateService(
                    provider,
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    new IUIWindowBinder[] { binder });

                await service.OpenAsync(configuration);
                Assert.That(
                    await service.CloseAsync(ClosedWindowId),
                    Is.True,
                    "the close must still succeed after rejecting the re-entrant request");

                Assert.That(
                    binder.SelfCloseGuardHitCount,
                    Is.EqualTo(1),
                    "closing the same window from inside its OnStartClose dispatch must throw " +
                    "InvalidOperationException synchronously instead of being deferred");
                AuditInvariants.AssertSettled(service, harness.Layer, null, provider, 0, "re-entrant close");
            }
        }

        [Test]
        public async Task ReentrantClose_FromBinderFinishedClose_IsRejectedSynchronously()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect(ClosedWindowId, 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                var binder = new AuditBinder("SelfCloseFinishedProbe");
                binder.ThrowForWindowId = ClosedWindowId;
                binder.SelfCloseOnStage = (int)WindowStateCallbackType.OnFinishedClose;
                UIService service = harness.CreateService(
                    provider,
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    new IUIWindowBinder[] { binder });

                await service.OpenAsync(configuration);
                Assert.That(await service.CloseAsync(ClosedWindowId), Is.True, "the close must succeed");

                Assert.That(
                    binder.SelfCloseGuardHitCount,
                    Is.EqualTo(1),
                    "closing the same window from inside its OnFinishedClose dispatch must throw " +
                    "InvalidOperationException synchronously");
                AuditInvariants.AssertSettled(service, harness.Layer, null, provider, 0, "re-entrant close finished");
            }
        }

        [Test]
        public async Task BinderRegistration_IsRejectedWhileSessionsAreActive()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect(ClosedWindowId, 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);
                await service.OpenAsync(configuration);

                Exception observed = null;
                try
                {
                    service.RegisterBinder(new AuditBinder("LateBinder"));
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "changing the binder set while a session lives would silently skip existing windows; " +
                    "it must throw, observed " + DescribeError(observed));

                await service.CloseAsync(ClosedWindowId);
                service.RegisterBinder(new AuditBinder("LateBinder"));
            }
        }

        // ------------------------------------------------------------------ navigation graph

        [Test]
        public void Navigation_RegisterRejectsDuplicateSelfReferenceAndMissingOpener()
        {
            var navigation = new UINavigationService(8);

            Assert.That(navigation.Register("Root"), Is.True, "the first registration must succeed");
            Assert.That(navigation.Register("Root"), Is.False, "a duplicate registration must be rejected");
            Assert.That(navigation.Register("Self", "Self"), Is.False, "a self-referencing edge must be rejected");
            Assert.That(
                navigation.Register("Orphan", "Missing"),
                Is.False,
                "an edge to a non-active opener must be rejected");
            Assert.That(navigation.Register(null), Is.False, "a null id must be rejected");
            Assert.That(navigation.Register(string.Empty), Is.False, "an empty id must be rejected");

            var history = new List<UINavigationEntry>(4);
            Assert.That(
                navigation.CopyHistory(history),
                Is.EqualTo(1),
                "rejected registrations must not mutate the graph");
        }

        [Test]
        public async Task Navigation_ReparentMovesChildrenToGrandparent()
        {
            using (var harness = new AuditHarness())
            {
                UINavigationService navigation = harness.CreateNavigation(8);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation), null);

                await service.OpenAsync(harness.CreateDirect("NavA", 0, false), new UIOpenOptions());
                await service.OpenAsync(
                    harness.CreateDirect("NavB", 0, false), new UIOpenOptions(openerId: "NavA"));
                await service.OpenAsync(
                    harness.CreateDirect("NavC", 0, false), new UIOpenOptions(openerId: "NavB"));

                Assert.That(navigation.GetOpener("NavC"), Is.EqualTo("NavB"), "NavC must start under NavB");

                await service.CloseAsync("NavB", ChildClosePolicy.Reparent);

                Assert.That(navigation.GetOpener("NavC"), Is.EqualTo("NavA"), "Reparent must reconnect NavC to NavA");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(2), "Reparent must keep the children alive");
                var children = new List<string>(4);
                Assert.That(navigation.CopyChildren("NavA", children), Is.EqualTo(1), "NavA must adopt NavC");
                Assert.That(children[0], Is.EqualTo("NavC"), "NavA's only child must be NavC");

                await service.CloseAsync("NavA", ChildClosePolicy.Reparent);
                await service.CloseAsync("NavC", ChildClosePolicy.Reparent);
                AuditInvariants.AssertSettled(service, harness.Layer, navigation, provider, 0, "reparent");
            }
        }

        [Test]
        public async Task Navigation_DetachClearsChildOpener()
        {
            using (var harness = new AuditHarness())
            {
                UINavigationService navigation = harness.CreateNavigation(8);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation), null);

                await service.OpenAsync(harness.CreateDirect("NavA", 0, false), new UIOpenOptions());
                await service.OpenAsync(
                    harness.CreateDirect("NavB", 0, false), new UIOpenOptions(openerId: "NavA"));
                await service.OpenAsync(
                    harness.CreateDirect("NavC", 0, false), new UIOpenOptions(openerId: "NavB"));

                await service.CloseAsync("NavB", ChildClosePolicy.Detach);

                Assert.That(navigation.GetOpener("NavC"), Is.Null, "Detach must turn children into root nodes");
                Assert.That(
                    navigation.ResolveBackTarget("NavC"),
                    Is.Null,
                    "a detached child must have no back target");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(2), "Detach must keep the children alive");

                await service.CloseAsync("NavA", ChildClosePolicy.Detach);
                await service.CloseAsync("NavC", ChildClosePolicy.Detach);
                AuditInvariants.AssertSettled(service, harness.Layer, navigation, provider, 0, "detach");
            }
        }

        [Test]
        public async Task Navigation_CascadeClosesEntireSubtreeChildrenFirst()
        {
            using (var harness = new AuditHarness())
            {
                UINavigationService navigation = harness.CreateNavigation(8);
                AuditAssetProvider provider = harness.CreateProvider();
                var trace = new AuditBinder("CascadeTrace");
                UIService service = harness.CreateService(
                    provider,
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation),
                    new IUIWindowBinder[] { trace });

                await service.OpenAsync(harness.CreateDirect("NavA", 0, false), new UIOpenOptions());
                await service.OpenAsync(
                    harness.CreateDirect("NavB", 0, false), new UIOpenOptions(openerId: "NavA"));
                await service.OpenAsync(
                    harness.CreateDirect("NavC", 0, false), new UIOpenOptions(openerId: "NavB"));
                Assert.That(service.ActiveWindowCount, Is.EqualTo(3), "three windows must be active");

                // Reset the trace so only close-stage events are ordered below.
                trace.Events.Clear();

                Assert.That(
                    await service.CloseAsync("NavA", ChildClosePolicy.Cascade),
                    Is.True,
                    "cascade close must report true");

                Assert.That(service.ActiveWindowCount, Is.EqualTo(0), "cascade must close the whole subtree");
                var history = new List<UINavigationEntry>(4);
                Assert.That(navigation.CopyHistory(history), Is.EqualTo(0), "cascade must empty the graph");

                var closeOrder = new List<string>(3);
                for (int i = 0; i < trace.Events.Count; i++)
                {
                    string entry = trace.Events[i];
                    if (entry.StartsWith("CascadeTrace:OnStartClose:", StringComparison.Ordinal))
                    {
                        closeOrder.Add(entry.Substring("CascadeTrace:OnStartClose:".Length));
                    }
                }

                CollectionAssert.AreEqual(
                    new[] { "NavC", "NavB", "NavA" },
                    closeOrder,
                    "cascade must close descendants before their parent (children-first teardown)");

                AuditInvariants.AssertSettled(service, harness.Layer, navigation, provider, 0, "cascade");
            }
        }

        // ------------------------------------------------------------------ navigation coordination

        [Test]
        public async Task Navigation_SecondConcurrentNavigateThrows()
        {
            using (var harness = new AuditHarness())
            {
                UINavigationService navigation = harness.CreateNavigation(8);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation), null);
                await service.OpenAsync(harness.CreateDirect("NavLeaving", 0, false), new UIOpenOptions());
                await service.OpenAsync(harness.CreateDirect("NavEntering", 0, false), new UIOpenOptions());

                var gated = new AuditCoordinator();
                gated.Gate = new UniTaskCompletionSource();
                UniTask<UIWindow> first = service.NavigateAsync(
                    "NavLeaving", "NavEntering", gated);
                Assert.That(
                    first.Status,
                    Is.EqualTo(UniTaskStatus.Pending),
                    "the gated coordinator must hold the navigation in flight");

                Exception observed = null;
                try
                {
                    service.NavigateAsync("NavLeaving", "NavEntering", new AuditCoordinator());
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "a second coordinated navigation must be rejected, observed " + DescribeError(observed));
                Assert.That(
                    observed.Message.Contains("already in progress"),
                    Is.True,
                    "the rejection must name the in-flight navigation, message='" + observed.Message + "'");

                gated.Gate.TrySetResult();
                UIWindow entering = await first;
                Assert.That(entering.WindowId, Is.EqualTo("NavEntering"), "the first navigation must commit");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(1), "navigation must leave exactly one window");
                AuditInvariants.AssertSettled(
                    service, harness.Layer, navigation, provider, 1, "after the gated navigation");
            }
        }

        [Test]
        public async Task Navigation_CoordinatorCancelRollsBackEnteringWindow()
        {
            using (var harness = new AuditHarness())
            {
                UINavigationService navigation = harness.CreateNavigation(8);
                AuditAssetProvider provider = harness.CreateProvider();
                UIWindowConfiguration enteringConfiguration =
                    harness.CreateProviderConfiguration("NavEntering", AuditDims.SourceAssetReference, 0);
                provider.Map(
                    "NavEntering", enteringConfiguration, harness.CreatePrefab("NavEntering"));
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation), null);
                UIWindow leaving = await service.OpenAsync(
                    harness.CreateDirect("NavLeaving", 0, false), new UIOpenOptions());

                var coordinator = new AuditCoordinator();
                coordinator.Cancel = true;
                Exception observed = null;
                try
                {
                    await service.NavigateAsync("NavLeaving", "NavEntering", coordinator);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<OperationCanceledException>(),
                    "a canceled coordinated transition must surface as OperationCanceledException, observed " +
                    DescribeError(observed));
                Assert.That(
                    referenceEqualsWindow(service, "NavLeaving", leaving),
                    Is.True,
                    "the leaving window must survive a canceled navigation");
                Assert.That(
                    service.ActiveWindowCount,
                    Is.EqualTo(1),
                    "the entering window must be rolled back");
                var history = new List<UINavigationEntry>(4);
                Assert.That(
                    navigation.CopyHistory(history),
                    Is.EqualTo(1),
                    "the rolled back entering window must be removed from the navigation graph");
                Assert.That(history[0].WindowId, Is.EqualTo("NavLeaving"), "only the leaving window may remain");
                AuditInvariants.AssertSettled(
                    service, harness.Layer, navigation, provider, 1, "after a canceled navigation");
            }
        }

        [Test]
        public async Task Navigation_CoordinatorThrowRollsBackAndAllowsNextNavigation()
        {
            using (var harness = new AuditHarness())
            {
                UINavigationService navigation = harness.CreateNavigation(8);
                AuditAssetProvider provider = harness.CreateProvider();
                UIWindowConfiguration enteringConfiguration =
                    harness.CreateProviderConfiguration("NavEntering", AuditDims.SourceAssetReference, 0);
                provider.Map(
                    "NavEntering", enteringConfiguration, harness.CreatePrefab("NavEntering"));
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation), null);
                await service.OpenAsync(harness.CreateDirect("NavLeaving", 0, false), new UIOpenOptions());

                var throwing = new AuditCoordinator();
                throwing.ThrowMessage = "Audit coordinator failure.";
                Exception observed = null;
                try
                {
                    await service.NavigateAsync("NavLeaving", "NavEntering", throwing);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "a coordinator fault must surface unchanged, observed " + DescribeError(observed));
                Assert.That(
                    service.ActiveWindowCount,
                    Is.EqualTo(1),
                    "the entering window must be rolled back after a coordinator fault");

                // The _isNavigating flag must be reset by the rollback path.
                var retry = new AuditCoordinator();
                UIWindow entering = await service.NavigateAsync("NavLeaving", "NavEntering", retry);
                Assert.That(
                    entering.WindowId,
                    Is.EqualTo("NavEntering"),
                    "a navigation after a coordinator fault must be allowed to run");
                Assert.That(retry.CallCount, Is.EqualTo(1), "the retry must reach the coordinator");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(1), "only the entering window may remain");
                AuditInvariants.AssertSettled(
                    service, harness.Layer, navigation, provider, 1, "after a coordinator fault retry");
            }
        }

        // ------------------------------------------------------------------ shutdown

        [Test]
        public async Task Shutdown_Immediate_ReleasesEverythingAndIsIdempotent()
        {
            using (var harness = new AuditHarness())
            {
                UINavigationService navigation = harness.CreateNavigation(8);
                AuditAssetProvider provider = harness.CreateProvider();
                UIWindowConfiguration first = harness.CreateProviderConfiguration(
                    "ShutdownA", AuditDims.SourceAssetReference, 0);
                provider.Map("ShutdownA", first, harness.CreatePrefab("ShutdownA"));
                UIWindowConfiguration second = harness.CreateProviderConfiguration(
                    "ShutdownB", AuditDims.SourcePathLocation, 0);
                provider.Map("ShutdownB", second, harness.CreatePrefab("ShutdownB"));
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation), null);

                await service.OpenAsync("ShutdownA");
                await service.OpenAsync("ShutdownB");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(2), "two windows must be active");

                await service.ShutdownAsync(UIShutdownMode.Immediate);

                Assert.That(service.IsDisposed, Is.True, "immediate shutdown must dispose the service");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(0), "no window may survive shutdown");
                Assert.That(harness.Layer.WindowCount, Is.EqualTo(0), "the layer must be emptied");
                Assert.That(
                    AuditHarness.CountAttachedWindowObjects(harness.Layer),
                    Is.EqualTo(0),
                    "(e) every window GameObject must be destroyed");
                Assert.That(provider.ConfigurationDisposeCount, Is.EqualTo(2), "both configuration leases released");
                Assert.That(provider.PrefabDisposeCount, Is.EqualTo(2), "both prefab leases released");
                var history = new List<UINavigationEntry>(4);
                Assert.That(navigation.CopyHistory(history), Is.EqualTo(0), "(f) the graph must be cleared");

                // Second shutdown must be a completed no-op, not a fault.
                await service.ShutdownAsync(UIShutdownMode.Immediate);
                await service.ShutdownAsync(UIShutdownMode.Animated);
                Assert.That(service.IsDisposed, Is.True, "repeated shutdown must stay idempotent");
            }
        }

        [Test]
        public async Task Shutdown_Animated_ClosesThroughTransitionsThenDisposes()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration first = harness.CreateDirect("AnimatedA", 0, false);
                UIWindowConfiguration second = harness.CreateDirect("AnimatedB", 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                var driver = new AuditTransitionDriver();
                UIServiceOptions options = AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null);
                options.DefaultTransitionDriver = driver;
                UIService service = harness.CreateService(provider, options, null);

                await service.OpenAsync(first);
                await service.OpenAsync(second);
                Assert.That(driver.OpenCallCount, Is.EqualTo(2), "both opens must run the transition");

                await service.ShutdownAsync(UIShutdownMode.Animated);

                Assert.That(
                    driver.CloseCallCount,
                    Is.EqualTo(2),
                    "animated shutdown must run a close transition for every window");
                Assert.That(service.IsDisposed, Is.True, "animated shutdown must dispose the service");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(0), "no window may survive shutdown");
                Assert.That(harness.Layer.WindowCount, Is.EqualTo(0), "the layer must be emptied");
            }
        }

        // ------------------------------------------------------------------ external destruction

        [Test]
        public async Task ExternalDestroy_OfWindowGameObject_IsObservedAndCleanedUp()
        {
            using (var harness = new AuditHarness())
            {
                UINavigationService navigation = harness.CreateNavigation(8);
                AuditAssetProvider provider = harness.CreateProvider();
                UIWindowConfiguration configuration = harness.CreateProviderConfiguration(
                    "ExternalDestroy", AuditDims.SourceAssetReference, 0);
                provider.Map("ExternalDestroy", configuration, harness.CreatePrefab("ExternalDestroy"));
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation), null);
                UIWindow window = await service.OpenAsync("ExternalDestroy");

                UnityEngine.Object.DestroyImmediate(window.gameObject);
                // The service reconciles destroyed windows on its next public touch; Editor batch
                // runs cannot rely on Unity delivering OnDestroy for DestroyImmediate.
                service.ReconcileDestroyedWindows();

                Assert.That(
                    service.ActiveWindowCount,
                    Is.EqualTo(0),
                    "an externally destroyed window must be removed from the session table");
                Assert.That(
                    service.GetPerformanceStats().SessionCount,
                    Is.EqualTo(0),
                    "the session must be released when the window GameObject is destroyed externally");
                Assert.That(harness.Layer.WindowCount, Is.EqualTo(0), "the layer must detach the destroyed window");
                Assert.That(provider.ConfigurationDisposeCount, Is.EqualTo(1), "the configuration lease must be released");
                Assert.That(provider.PrefabDisposeCount, Is.EqualTo(1), "the prefab lease must be released");
                var history = new List<UINavigationEntry>(4);
                Assert.That(navigation.CopyHistory(history), Is.EqualTo(0), "(f) the graph must drop the window");

                Assert.That(
                    await service.CloseAsync("ExternalDestroy"),
                    Is.False,
                    "closing an already externally destroyed window must report false");
            }
        }

        // ------------------------------------------------------------------ scene binding

        [Test]
        public async Task SceneBound_WindowWithMismatchedHandle_IsClosedBySceneSweep()
        {
            using (var harness = new AuditHarness())
            {
                UINavigationService navigation = harness.CreateNavigation(8);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation), null);

                // SceneBoundOverride=true makes the window bind to the active scene without
                // switching scenes, which keeps this test deterministic in EditMode.
                UIWindow window = await service.OpenAsync(
                    harness.CreateDirect("SceneBoundWindow", 0, false),
                    new UIOpenOptions(null, null, true, default, null, false));

                int activeHandle = SceneManager.GetActiveScene().handle;
                Assert.That(window.IsSceneBound, Is.True, "the override must mark the window as scene bound");
                Assert.That(window.BoundSceneHandle, Is.EqualTo(activeHandle), "the window must capture the active handle");

                int foreignHandle = activeHandle == 0 ? 1 : activeHandle + 1;
                await InvokeSceneSweepAsync(service, foreignHandle);

                Assert.That(
                    service.ActiveWindowCount,
                    Is.EqualTo(0),
                    "a scene-bound window whose owner scene changed must be closed by the sweep");
                Assert.That(harness.Layer.WindowCount, Is.EqualTo(0), "the layer must be emptied");
                AuditInvariants.AssertSettled(
                    service, harness.Layer, navigation, provider, 0, "scene-bound sweep closed the window");
            }
        }

        [Test]
        public async Task SceneBound_WindowWithMatchingHandle_IsKeptBySceneSweep()
        {
            using (var harness = new AuditHarness())
            {
                UINavigationService navigation = harness.CreateNavigation(8);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation), null);

                UIWindow window = await service.OpenAsync(
                    harness.CreateDirect("SceneBoundKeeper", 0, false),
                    new UIOpenOptions(null, null, true, default, null, false));
                int activeHandle = SceneManager.GetActiveScene().handle;

                await InvokeSceneSweepAsync(service, activeHandle);

                Assert.That(
                    service.ActiveWindowCount,
                    Is.EqualTo(1),
                    "a scene-bound window that still belongs to the active scene must survive the sweep");
                Assert.That(
                    referenceEqualsWindow(service, "SceneBoundKeeper", window),
                    Is.True,
                    "the surviving instance must be the original window");
                AuditInvariants.AssertSettled(
                    service, harness.Layer, navigation, provider, 1, "scene-bound sweep kept the window");

                await service.CloseAsync("SceneBoundKeeper");
            }
        }

        [Test]
        public async Task SceneBoundOverrideFalse_ClearsSceneBindingOfAConfiguredBoundWindow()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);
                UIWindowConfiguration configuration = harness.CreateDirect("BoundByConfiguration", 0, true);
                Assert.That(configuration.IsSceneBound, Is.True, "the configuration must request scene binding");

                UIWindow window = await service.OpenAsync(
                    configuration,
                    new UIOpenOptions(null, null, false, default, null, false));

                Assert.That(window.IsSceneBound, Is.False, "SceneBoundOverride=false must win over the configuration");
                Assert.That(window.BoundSceneHandle, Is.EqualTo(-1), "an unbound window must report handle -1");
                Assert.That(
                    service.GetPerformanceStats().SceneBoundWindowCount,
                    Is.EqualTo(0),
                    "scene-bound accounting must follow the effective flag");

                await service.CloseAsync("BoundByConfiguration");
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Invokes the private scene-bound sweep directly. EditMode cannot switch scenes without
        /// destabilising the shared editor scene, so the sweep contract is exercised through the
        /// same entry point the SceneManager callback uses.
        /// </summary>
        private static UniTask InvokeSceneSweepAsync(UIService service, int activeSceneHandle)
        {
            object result = TestReflection.Invoke(service, "CloseSceneBoundWindowsAsync", activeSceneHandle);
            return (UniTask)result;
        }

        private static bool referenceEqualsWindow(UIService service, string windowId, UIWindow expected)
        {
            UIWindow found;
            if (!service.TryGetWindow(windowId, out found))
            {
                return false;
            }

            return ReferenceEquals(found, expected);
        }

        /// <summary>
        /// Captures a synchronous exception from a service entry point. The try/catch is written
        /// out at each call site instead of through a delegate so this suite stays free of both
        /// lambda expressions and reflection-based invocation for behavioural assertions.
        /// </summary>
        private static string DescribeError(Exception exception)
        {
            return exception == null
                ? "<no exception>"
                : exception.GetType().Name + ": " + exception.Message;
        }
    }
}
