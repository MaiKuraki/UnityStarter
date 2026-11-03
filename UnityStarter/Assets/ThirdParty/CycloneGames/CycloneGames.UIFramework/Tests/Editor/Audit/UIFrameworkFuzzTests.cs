using System;
using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;

namespace CycloneGames.UIFramework.Tests.Editor.Audit
{
    /// <summary>
    /// Deterministic random walk over the public service surface. Faults are limited to a
    /// whitelist, ownership is verified after every step, and the walk must end recoverable.
    /// Randomness uses explicit System.Random(seed) instances so a failure is reproducible.
    /// </summary>
    [Category("UIFrameworkAudit")]
    public sealed class UIFrameworkFuzzTests
    {
        private const int StepsPerSeed = 2000;
        private const int TimeoutMilliseconds = 300000;
        private const int IdPoolSize = 8;
        private const int FirstAssetIdIndex = 3;
        private const int FirstPathIdIndex = 6;
        private const int CapacityRelaxed = 8;

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(42)]
        [TestCase(1337)]
        [TestCase(99991)]
        [Timeout(TimeoutMilliseconds)]
        public void Fuzz_RandomPublicOperations_KeepEveryInvariant(int seed)
        {
            var tallies = new Dictionary<string, int>(8, StringComparer.Ordinal);
            using (var harness = new AuditHarness())
            {
                FuzzContext context = CreateContext(harness);
                var random = new System.Random(seed);
                int operations = 0;

                for (int step = 0; step < StepsPerSeed; step++)
                {
                    int operation = random.Next(0, 6);
                    string label = "seed=" + seed + " step=" + step;
                    Exception fault = null;

                    try
                    {
                        fault = ExecuteStep(context, random, operation, label);
                    }
                    catch (Exception exception)
                    {
                        fault = exception;
                    }

                    operations++;

                    if (fault != null)
                    {
                        RecordFault(tallies, fault, label, operation);
                    }

                    // Invariants are checked outside the fault handler so a broken invariant can
                    // never be mistaken for an allowed service fault.
                    AuditInvariants.CheckConsistent(
                        context.Service,
                        context.Layer,
                        context.Navigation,
                        context.Provider,
                        label + " op=" + operation);
                }

                TestContext.Progress.WriteLine(
                    "FUZZ,seed=" + seed + ",operations=" + operations +
                    ",activeAtEnd=" + context.Service.ActiveWindowCount +
                    ",faults=" + DescribeTallies(tallies));

                context.Service.ShutdownAsync(UIShutdownMode.Immediate).GetAwaiter().GetResult();

                Assert.That(
                    context.Service.IsDisposed,
                    Is.True,
                    "seed=" + seed + " | ShutdownAsync(Immediate) must dispose the service after the walk");
                Assert.That(
                    context.Service.ActiveWindowCount,
                    Is.EqualTo(0),
                    "seed=" + seed + " | the walk must be recoverable: ActiveWindowCount must be 0 after shutdown");
                Assert.That(
                    context.Layer.WindowCount,
                    Is.EqualTo(0),
                    "seed=" + seed + " | the layer must be empty after shutdown");
                Assert.That(
                    AuditHarness.CountAttachedWindowObjects(context.Layer),
                    Is.EqualTo(0),
                    "seed=" + seed + " | every window GameObject must be destroyed by shutdown");
                Assert.That(
                    context.Navigation.CopyHistory(context.HistoryScratch),
                    Is.EqualTo(0),
                    "seed=" + seed + " | the navigation graph must be empty after shutdown");
                Assert.That(
                    AuditInvariants.LeaseBalance(context.Provider),
                    Is.True,
                    "seed=" + seed + " | provider leases must balance after shutdown: config " +
                    context.Provider.ConfigurationDisposeCount + "/" +
                    context.Provider.ConfigurationAcquireCount + " prefab " +
                    context.Provider.PrefabDisposeCount + "/" + context.Provider.PrefabAcquireCount +
                    " doubleDispose=" + context.Provider.DoubleDisposeCount);
                Assert.That(
                    context.Binder.DisposeCount,
                    Is.EqualTo(context.Binder.BindCount),
                    "seed=" + seed + " | every binding must be disposed exactly once: bound=" +
                    context.Binder.BindCount + " disposed=" + context.Binder.DisposeCount);
            }
        }

        /// <summary>
        /// Runs one random operation and returns the fault it produced, if any.
        /// </summary>
        private static Exception ExecuteStep(
            FuzzContext context,
            System.Random random,
            int operation,
            string label)
        {
            switch (operation)
            {
                case 0:
                    return StepOpen(context, random);
                case 1:
                    return StepClose(context, random);
                case 2:
                    return StepNavigate(context, random);
                case 3:
                    return StepRegisterBinder(context);
                case 4:
                    return StepQuery(context, random, label);
                default:
                    return StepDuplicateOpen(context, random);
            }
        }

        private static Exception StepOpen(FuzzContext context, System.Random random)
        {
            int idIndex = random.Next(0, IdPoolSize);
            return OpenById(context, idIndex, random);
        }

        private static Exception StepDuplicateOpen(FuzzContext context, System.Random random)
        {
            UIWindow active;
            if (context.Service.CopyActiveWindows(context.WindowScratch) == 0)
            {
                return null;
            }

            active = context.WindowScratch[random.Next(0, context.WindowScratch.Count)];
            if (active == null)
            {
                return null;
            }

            try
            {
                // Re-opening an active id must either join the session or raise a classified fault.
                context.Service.OpenAsync(active.WindowId, BuildOptions(context, random))
                    .GetAwaiter()
                    .GetResult();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static Exception StepClose(FuzzContext context, System.Random random)
        {
            int idIndex = random.Next(0, IdPoolSize);
            int policyIndex = random.Next(0, 8);
            ChildClosePolicy policy;
            switch (policyIndex)
            {
                case 1:
                    policy = ChildClosePolicy.Cascade;
                    break;
                case 2:
                    policy = ChildClosePolicy.Detach;
                    break;
                case 7:
                    // Undefined policy value: the Try-style contract must reject it without
                    // corrupting the graph, and the close must still release the window.
                    policy = (ChildClosePolicy)999;
                    break;
                default:
                    policy = ChildClosePolicy.Reparent;
                    break;
            }

            try
            {
                context.Service.CloseAsync(context.WindowIds[idIndex], policy)
                    .GetAwaiter()
                    .GetResult();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static Exception StepNavigate(FuzzContext context, System.Random random)
        {
            int leavingIndex = random.Next(0, IdPoolSize);
            int enteringIndex = random.Next(0, IdPoolSize);
            string leavingId = context.WindowIds[leavingIndex];
            string enteringId = context.WindowIds[enteringIndex];
            AuditCoordinator coordinator = context.Coordinators[random.Next(0, context.Coordinators.Length)];

            try
            {
                context.Service.NavigateAsync(leavingId, enteringId, coordinator)
                    .GetAwaiter()
                    .GetResult();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static Exception StepRegisterBinder(FuzzContext context)
        {
            try
            {
                // Allowed only while no session exists; otherwise the guard must reject it.
                context.Service.RegisterBinder(new AuditBinder("FuzzLateBinder"));
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static Exception StepQuery(FuzzContext context, System.Random random, string label)
        {
            int idIndex = random.Next(0, IdPoolSize);
            string windowId = context.WindowIds[idIndex];

            try
            {
                UIWindow window;
                context.Service.TryGetWindow(windowId, out window);
                context.Service.TryGetWindow("FuzzMissing", out window);
                context.Service.CopyActiveWindows(context.WindowScratch);
                context.Service.GetPerformanceStats();
                context.Service.CopyLayerRuntimeStats(context.LayerScratch);
                context.Navigation.CopyHistory(context.HistoryScratch);
                context.Navigation.CopyAncestors(windowId, context.IdScratch);
                context.Navigation.CopyChildren(windowId, context.IdScratch);
                context.Navigation.ResolveBackTarget(windowId);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static Exception OpenById(FuzzContext context, int idIndex, System.Random random)
        {
            try
            {
                UIOpenOptions options = BuildOptions(context, random);
                if (context.Sources[idIndex] == AuditDims.SourcePrefabReference)
                {
                    context.Service.OpenAsync(context.Configurations[idIndex], options)
                        .GetAwaiter()
                        .GetResult();
                }
                else
                {
                    context.Service.OpenAsync(context.WindowIds[idIndex], options)
                        .GetAwaiter()
                        .GetResult();
                }

                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static UIOpenOptions BuildOptions(FuzzContext context, System.Random random)
        {
            string openerId = null;
            if (random.Next(0, 4) == 0 && context.Service.CopyActiveWindows(context.WindowScratch) > 0)
            {
                UIWindow opener = context.WindowScratch[random.Next(0, context.WindowScratch.Count)];
                if (opener != null)
                {
                    openerId = opener.WindowId;
                }
            }

            object payload = random.Next(0, 3) == 0 ? new object() : null;
            bool? sceneBoundOverride = null;
            int sceneChoice = random.Next(0, 3);
            if (sceneChoice == 1)
            {
                sceneBoundOverride = true;
            }
            else if (sceneChoice == 2)
            {
                sceneBoundOverride = false;
            }

            bool suppressTransition = random.Next(0, 3) == 0;
            IUIWindowTransitionDriver driver = context.Drivers[random.Next(0, context.Drivers.Length)];

            return new UIOpenOptions(openerId, payload, sceneBoundOverride, default, driver, suppressTransition);
        }

        private static void RecordFault(
            Dictionary<string, int> tallies,
            Exception exception,
            string label,
            int operation)
        {
            string typeName = exception.GetType().Name;
            int count;
            if (tallies.TryGetValue(typeName, out count))
            {
                tallies[typeName] = count + 1;
            }
            else
            {
                tallies[typeName] = 1;
            }

            if (IsWhitelisted(exception))
            {
                return;
            }

            Assert.Fail(
                label + " op=" + operation + " | an unexpected fault type escaped the service: " +
                typeName + ": " + exception.Message +
                "\nAllowed fault types are InvalidOperationException, ArgumentException, " +
                "ObjectDisposedException, OperationCanceledException and AggregateException.");
        }

        private static bool IsWhitelisted(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                return true;
            }

            if (exception is ArgumentException)
            {
                return true;
            }

            if (exception is AggregateException)
            {
                return true;
            }

            // ObjectDisposedException derives from InvalidOperationException but is listed
            // explicitly so the whitelist stays readable against the audit brief.
            if (exception is ObjectDisposedException)
            {
                return true;
            }

            return exception is InvalidOperationException;
        }

        private static string DescribeTallies(Dictionary<string, int> tallies)
        {
            if (tallies.Count == 0)
            {
                return "none";
            }

            var builder = new System.Text.StringBuilder(128);
            foreach (KeyValuePair<string, int> entry in tallies)
            {
                if (builder.Length > 0)
                {
                    builder.Append('|');
                }

                builder.Append(entry.Key);
                builder.Append('=');
                builder.Append(entry.Value);
            }

            return builder.ToString();
        }

        private static FuzzContext CreateContext(AuditHarness harness)
        {
            var context = new FuzzContext();
            context.Layer = harness.Layer;
            context.Provider = harness.CreateProvider();
            context.Navigation = harness.CreateNavigation(16);
            context.Binder = new AuditBinder("FuzzBinder");
            context.Binder.RecordEvents = false;

            context.WindowIds = new string[IdPoolSize];
            context.Sources = new int[IdPoolSize];
            context.Configurations = new UIWindowConfiguration[IdPoolSize];
            context.WindowScratch = new List<UIWindow>(CapacityRelaxed);
            context.LayerScratch = new List<UILayerRuntimeStats>(8);
            context.HistoryScratch = new List<UINavigationEntry>(16);
            context.IdScratch = new List<string>(16);

            for (int i = 0; i < IdPoolSize; i++)
            {
                string windowId = "Fuzz" + i.ToString("D2");
                int source;
                if (i < FirstAssetIdIndex)
                {
                    source = AuditDims.SourcePrefabReference;
                }
                else if (i < FirstPathIdIndex)
                {
                    source = AuditDims.SourceAssetReference;
                }
                else
                {
                    source = AuditDims.SourcePathLocation;
                }

                UIWindowConfiguration configuration = source == AuditDims.SourcePrefabReference
                    ? harness.CreateDirect(windowId, i, false)
                    : harness.CreateProviderConfiguration(windowId, source, i);
                AuditHarness.ApplyCanvasPolicy(
                    configuration, i % 2 == 0 ? AuditDims.CanvasIsolated : AuditDims.CanvasInherit);
                AuditHarness.ApplyCullWhenHidden(
                    configuration, i % 3 == 0 ? AuditDims.CullEnabled : AuditDims.CullDisabled);

                context.WindowIds[i] = windowId;
                context.Sources[i] = source;
                context.Configurations[i] = configuration;
                context.Provider.Map(windowId, configuration, harness.CreatePrefab(windowId));
            }

            var syncDriver = new AuditTransitionDriver();
            var throwingDriver = new AuditTransitionDriver();
            throwingDriver.OpenThrowMessage = "Fuzz open transition failure.";
            var cancelDriver = new AuditTransitionDriver();
            cancelDriver.CancelOpen = true;
            context.Drivers = new IUIWindowTransitionDriver[] { null, syncDriver, throwingDriver, cancelDriver };

            var syncCoordinator = new AuditCoordinator();
            var throwingCoordinator = new AuditCoordinator();
            throwingCoordinator.ThrowMessage = "Fuzz coordinator failure.";
            var cancelCoordinator = new AuditCoordinator();
            cancelCoordinator.Cancel = true;
            context.Coordinators = new[]
            {
                syncCoordinator,
                throwingCoordinator,
                cancelCoordinator,
            };

            context.Service = harness.CreateService(
                context.Provider,
                AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, context.Navigation),
                new IUIWindowBinder[] { context.Binder });

            return context;
        }

        private sealed class FuzzContext
        {
            public UILayer Layer;
            public AuditAssetProvider Provider;
            public UINavigationService Navigation;
            public AuditBinder Binder;
            public UIService Service;
            public string[] WindowIds;
            public int[] Sources;
            public UIWindowConfiguration[] Configurations;
            public IUIWindowTransitionDriver[] Drivers;
            public AuditCoordinator[] Coordinators;
            public List<UIWindow> WindowScratch;
            public List<UILayerRuntimeStats> LayerScratch;
            public List<UINavigationEntry> HistoryScratch;
            public List<string> IdScratch;
        }
    }
}
