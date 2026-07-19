using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GameplayCueCommittedTests
    {
        private const string CueTagName = "Test.GAS.Cue.Committed";

        private GameplayTag cueTag;
        private ProbeCueManager cueManager;
        private GASRuntimeContext context;
        private AbilitySystemComponent abilitySystem;

        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.RegisterDynamicTag(CueTagName, "Committed Gameplay Cue test tag.");
            GameplayTagManager.InitializeIfNeeded();
            cueTag = GameplayTagManager.RequestTag(CueTagName);
            cueManager = new ProbeCueManager();
            context = new GASRuntimeContext(
                GASRuntimeAuthorityMode.Authority,
                cueManager: cueManager);
            abilitySystem = new AbilitySystemComponent(
                context,
                GASAbilitySystemRuntimeOptions.RuntimeOnly);
        }

        [TearDown]
        public void TearDown()
        {
            abilitySystem?.Dispose();
            context?.Dispose();
        }

        [Test]
        public void PersistentCue_UsesSamePositiveReconciliationIdForActiveAndRemoved()
        {
            var observer = new CueObserver();
            abilitySystem.OnGameplayCueCommitted += observer.Record;
            GameplayEffect effect = CreateCueEffect("Cue.Persistent", EDurationPolicy.Infinite);

            GameplayEffectApplicationResult application = abilitySystem.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(effect, abilitySystem));
            Assert.That(application.Succeeded, Is.True);
            Assert.That(observer.Cues.Count, Is.EqualTo(1));
            Assert.That(cueManager.InvocationCount, Is.EqualTo(1));
            Assert.That(cueManager.LastCue, Is.EqualTo(cueTag));
            GameplayCueCommitted active = observer.Cues[0];
            Assert.That(active.Event, Is.EqualTo(EGameplayCueEvent.OnActive));
            Assert.That(active.ActiveEffectReconciliationId, Is.GreaterThan(0));
            Assert.That(active.StateVersion, Is.EqualTo(abilitySystem.StateVersion));

            int reconciliationId = active.ActiveEffectReconciliationId;
            Assert.That(abilitySystem.TryRemoveActiveEffect(application.ActiveEffect), Is.True);
            Assert.That(observer.Cues.Count, Is.EqualTo(2));
            GameplayCueCommitted removed = observer.Cues[1];
            Assert.That(removed.Event, Is.EqualTo(EGameplayCueEvent.Removed));
            Assert.That(removed.ActiveEffectReconciliationId, Is.EqualTo(reconciliationId));
            Assert.That(removed.StateVersion, Is.GreaterThan(active.StateVersion));
        }

        [Test]
        public void InstantCue_UsesZeroReconciliationId()
        {
            var observer = new CueObserver();
            abilitySystem.OnGameplayCueCommitted += observer.Record;

            GameplayEffectApplicationResult application = abilitySystem.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(
                    CreateCueEffect("Cue.Instant", EDurationPolicy.Instant),
                    abilitySystem));

            Assert.That(application.Succeeded, Is.True);
            Assert.That(observer.Cues.Count, Is.EqualTo(1));
            Assert.That(observer.Cues[0].Event, Is.EqualTo(EGameplayCueEvent.Executed));
            Assert.That(observer.Cues[0].ActiveEffectReconciliationId, Is.Zero);
        }

        [Test]
        public void PeriodicExecution_PublishesExecutedWithActiveEffectIdentity()
        {
            var observer = new CueObserver();
            abilitySystem.OnGameplayCueCommitted += observer.Record;
            GameplayEffect effect = CreateCueEffect(
                "Cue.Periodic",
                EDurationPolicy.Infinite,
                period: 0.25f,
                executePeriodicEffectOnApplication: true);
            GameplayEffectApplicationResult application = abilitySystem.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(effect, abilitySystem));
            Assert.That(application.Succeeded, Is.True);
            int reconciliationId = application.ActiveEffect.ReconciliationId;
            observer.Cues.Clear();

            abilitySystem.Tick(0f, isServer: true);

            Assert.That(observer.Cues.Count, Is.EqualTo(1));
            Assert.That(observer.Cues[0].Event, Is.EqualTo(EGameplayCueEvent.Executed));
            Assert.That(observer.Cues[0].ActiveEffectReconciliationId, Is.EqualTo(reconciliationId));
        }

        [Test]
        public void ObserverAndPresentationExceptions_DoNotSuppressLaterCommittedObserver()
        {
            var throwingObserver = new CueObserver { ThrowOnRecord = true };
            var recordingObserver = new CueObserver();
            cueManager.ThrowOnHandle = true;
            abilitySystem.OnGameplayCueCommitted += throwingObserver.Record;
            abilitySystem.OnGameplayCueCommitted += recordingObserver.Record;

            GameplayEffectApplicationResult application = abilitySystem.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(
                    CreateCueEffect("Cue.Exceptions", EDurationPolicy.Instant),
                    abilitySystem));

            Assert.That(application.Succeeded, Is.True);
            Assert.That(throwingObserver.InvocationCount, Is.EqualTo(1));
            Assert.That(recordingObserver.Cues.Count, Is.EqualTo(1));
            Assert.That(recordingObserver.Cues[0].Event, Is.EqualTo(EGameplayCueEvent.Executed));
        }

        private GameplayEffect CreateCueEffect(
            string name,
            EDurationPolicy durationPolicy,
            float period = 0f,
            bool executePeriodicEffectOnApplication = true)
        {
            var cues = new GameplayTagContainer();
            cues.AddTag(cueTag);
            return new GameplayEffect(
                name,
                durationPolicy,
                period: period,
                gameplayCues: cues,
                executePeriodicEffectOnApplication: executePeriodicEffectOnApplication);
        }

        private sealed class CueObserver
        {
            public readonly List<GameplayCueCommitted> Cues = new List<GameplayCueCommitted>();
            public bool ThrowOnRecord;
            public int InvocationCount;

            public void Record(in GameplayCueCommitted cue)
            {
                InvocationCount++;
                if (ThrowOnRecord)
                    throw new InvalidOperationException("Injected committed-cue observer failure.");
                Cues.Add(cue);
            }
        }

        private sealed class ProbeCueManager : IGameplayCueManager
        {
            public bool ThrowOnHandle;
            public int InvocationCount;
            public GameplayTag LastCue;

            public void RegisterStaticCue(GameplayTag cueTag, string assetAddress) { }

            public void HandleCue(
                object asc,
                GameplayTag cueTag,
                EGameplayCueEvent eventType,
                GameplayCueEventParams parameters)
            {
                InvocationCount++;
                LastCue = cueTag;
                if (ThrowOnHandle)
                    throw new InvalidOperationException("Injected local cue presentation failure.");
            }

            public void RemoveAllCuesFor(object asc) { }
            public void CommitPredictedCues(object asc, GASPredictionKey predictionKey) { }
            public void RollbackPredictedCues(object asc, GASPredictionKey predictionKey) { }
        }
    }
}
