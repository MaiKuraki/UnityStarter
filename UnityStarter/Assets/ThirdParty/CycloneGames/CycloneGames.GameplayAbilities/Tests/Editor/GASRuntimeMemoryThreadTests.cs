using System;
using System.Threading;

using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASRuntimeMemoryThreadTests
    {
        private static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(5);

        [Test]
        public void LogWarningPolicy_CrossThreadAcquireFailsBeforeLeaseMutation()
        {
            var context = new GASRuntimeContext(threadPolicy: GASRuntimeThreadPolicy.LogWarning);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);

            try
            {
                GASRuntimeMemoryStatistics before = context.GetMemoryStatistics();
                Exception workerException = RunOnWorkerThread(
                    () => GameplayEffectSpec.Create(CreateDurationEffect("CrossThreadRent"), asc));

                Assert.That(workerException, Is.TypeOf<InvalidOperationException>());

                GASRuntimeMemoryStatistics after = context.GetMemoryStatistics();
                Assert.That(after.EffectSpecs.Acquisitions, Is.EqualTo(before.EffectSpecs.Acquisitions));
                Assert.That(after.EffectSpecs.Active, Is.EqualTo(before.EffectSpecs.Active));
                Assert.That(after.EffectContexts.Acquisitions, Is.EqualTo(before.EffectContexts.Acquisitions));
                Assert.That(after.EffectContexts.Active, Is.EqualTo(before.EffectContexts.Active));
            }
            finally
            {
                asc.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void ThrowPolicy_CrossThreadReleaseFailsBeforeLeaseMutation()
        {
            var context = new GASRuntimeContext(threadPolicy: GASRuntimeThreadPolicy.Throw);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayEffectSpec spec = GameplayEffectSpec.Create(CreateDurationEffect("CrossThreadReturn"), asc);

            try
            {
                GASRuntimeMemoryStatistics before = context.GetMemoryStatistics();
                Exception workerException = RunOnWorkerThread(spec.Discard);

                Assert.That(workerException, Is.TypeOf<InvalidOperationException>());

                GASRuntimeMemoryStatistics after = context.GetMemoryStatistics();
                Assert.That(after.EffectSpecs.Active, Is.EqualTo(before.EffectSpecs.Active));
                Assert.That(after.EffectSpecs.InvalidReleases, Is.EqualTo(before.EffectSpecs.InvalidReleases));
                Assert.That(after.EffectContexts.Active, Is.EqualTo(before.EffectContexts.Active));

                spec.Discard();
                spec = null;
                Assert.That(context.GetMemoryStatistics().OutstandingLeases, Is.EqualTo(0));
            }
            finally
            {
                spec?.Discard();
                asc.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void LogWarningPolicy_CrossThreadAbilitySystemConstructionFailsBeforeContextRegistration()
        {
            var context = new GASRuntimeContext(threadPolicy: GASRuntimeThreadPolicy.LogWarning);

            try
            {
                GASRuntimeMemoryStatistics before = context.GetMemoryStatistics();
                Exception workerException = RunOnWorkerThread(
                    () => _ = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly));

                Assert.That(workerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(context.RegisteredAbilitySystemCount, Is.Zero);
                GASRuntimeMemoryStatistics after = context.GetMemoryStatistics();
                Assert.That(after.OutstandingLeases, Is.EqualTo(before.OutstandingLeases));
                Assert.That(after.Abilities.Active, Is.EqualTo(before.Abilities.Active));
                Assert.That(after.Tasks.Active, Is.EqualTo(before.Tasks.Active));
            }
            finally
            {
                context.Dispose();
            }
        }

        [Test]
        public void LogWarningPolicy_CrossThreadAbilitySystemDisposeFailsWithoutMutationAndOwnerCanDispose()
        {
            var context = new GASRuntimeContext(threadPolicy: GASRuntimeThreadPolicy.LogWarning);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);

            GASRuntimeMemoryStatistics before = context.GetMemoryStatistics();
            Exception workerException = RunOnWorkerThread(asc.Dispose);

            Assert.That(workerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(asc.IsDisposed, Is.False);
            Assert.That(context.RegisteredAbilitySystemCount, Is.EqualTo(1));
            GASRuntimeMemoryStatistics afterWorker = context.GetMemoryStatistics();
            Assert.That(afterWorker.OutstandingLeases, Is.EqualTo(before.OutstandingLeases));
            Assert.That(afterWorker.Abilities.Active, Is.EqualTo(before.Abilities.Active));
            Assert.That(afterWorker.Tasks.Active, Is.EqualTo(before.Tasks.Active));

            asc.Dispose();
            Assert.That(asc.IsDisposed, Is.True);
            Assert.That(context.RegisteredAbilitySystemCount, Is.Zero);
            Assert.That(context.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            context.Dispose();
        }

        [Test]
        public void BoundGameplayAttribute_SubscriptionMutationRejectsWorkerThread()
        {
            var context = new GASRuntimeContext(threadPolicy: GASRuntimeThreadPolicy.LogWarning);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new ThreadAttributeSet();
            asc.AddAttributeSet(attributes);
            int callbackCount = 0;
            Action<float, float> callback = (_, __) => callbackCount++;

            try
            {
                Exception addException = RunOnWorkerThread(() => attributes.Resource.OnCurrentValueChanged += callback);
                Assert.That(addException, Is.TypeOf<InvalidOperationException>());

                attributes.Resource.OnCurrentValueChanged += callback;
                Exception removeException = RunOnWorkerThread(() => attributes.Resource.OnCurrentValueChanged -= callback);
                Assert.That(removeException, Is.TypeOf<InvalidOperationException>());

                attributes.Resource.SetCurrentValue(5f);
                Assert.That(callbackCount, Is.EqualTo(1));
                attributes.Resource.OnCurrentValueChanged -= callback;
            }
            finally
            {
                asc.Dispose();
                context.Dispose();
            }
        }

        private static Exception RunOnWorkerThread(Action action)
        {
            Exception exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception caught)
                {
                    exception = caught;
                }
            })
            {
                IsBackground = true
            };

            thread.Start();
            if (!thread.Join(WorkerTimeout))
            {
                throw new TimeoutException("The worker thread did not finish within the test timeout.");
            }

            return exception;
        }

        private static GameplayEffect CreateDurationEffect(string name)
        {
            return new GameplayEffect(name, EDurationPolicy.HasDuration, duration: 1f);
        }

        private sealed class ThreadAttributeSet : AttributeSet
        {
            public GameplayAttribute Resource { get; } = new GameplayAttribute("ThreadResource");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Resource);
            }
        }
    }
}
