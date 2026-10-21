using System;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GameplayCueDispatchIsolationTests
    {
        [Test]
        public void ThrowingHandler_DoesNotDropRemainingCues()
        {
            GameplayTagManager.RegisterDynamicTag("Test.Cue.Isolation.First", "Cue isolation probe.");
            GameplayTagManager.RegisterDynamicTag("Test.Cue.Isolation.Second", "Cue isolation probe.");
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag first = GameplayTagManager.Request("Test.Cue.Isolation.First");
            GameplayTag second = GameplayTagManager.Request("Test.Cue.Isolation.Second");

            var manager = new ThrowingCueManager(first);
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority, cueManager: manager);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                var cues = new GameplayTagContainer();
                cues.AddTag(first);
                cues.AddTag(second);

                var effect = new GameplayEffect(
                    "Cue.Isolation",
                    EDurationPolicy.Instant,
                    gameplayCues: cues);

                GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(effect, asc));

                Assert.That(result.Succeeded, Is.True);
                Assert.That(manager.Attempts, Is.EqualTo(2),
                    "A throwing handler must not drop the remaining cues of the same effect.");
            }
            finally
            {
                asc.Dispose();
                context.Dispose();
            }
        }

        private sealed class ThrowingCueManager : IGameplayCueManager
        {
            private readonly GameplayTag throwingTag;

            public ThrowingCueManager(GameplayTag throwingTag)
            {
                this.throwingTag = throwingTag;
            }

            public int Attempts { get; private set; }

            public void RegisterStaticCue(GameplayTag cueTag, string assetAddress)
            {
            }

            public void HandleCue(object asc, GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueEventParams parameters)
            {
                Attempts++;
                if (cueTag == throwingTag)
                {
                    throw new InvalidOperationException("Cue handler failure.");
                }
            }

            public void RemoveAllCuesFor(object asc)
            {
            }

            public void CommitPredictedCues(object asc, GASPredictionKey predictionKey)
            {
            }

            public void RollbackPredictedCues(object asc, GASPredictionKey predictionKey)
            {
            }
        }
    }
}
