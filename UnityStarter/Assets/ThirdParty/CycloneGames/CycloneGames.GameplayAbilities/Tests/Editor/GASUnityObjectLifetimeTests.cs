using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Unity's overloaded null check must be honored when framework code stores actor references;
    /// pattern matching and the null-coalescing operator bypass it and would keep destroyed objects.
    /// </summary>
    public sealed class GASUnityObjectLifetimeTests
    {
        [Test]
        public void CueParameters_DestroyedObjects_ResolveToNull()
        {
            var go = new GameObject("cue-object");
            Object.DestroyImmediate(go);

            var parameters = new GameplayCueParameters(new GameplayCueEventParams(
                null, null, null, go, go, 0, 0L));

            Assert.That(ReferenceEquals(parameters.SourceObject, null), Is.True);
            Assert.That(ReferenceEquals(parameters.TargetObject, null), Is.True);
        }

        [Test]
        public void ActorInfo_DestroyedOwnerAndAvatar_ResolveToNull()
        {
            var go = new GameObject("actor");
            Object.DestroyImmediate(go);

            var info = new GameplayAbilityActorInfo(go, go);

            Assert.That(ReferenceEquals(info.OwnerUnityObject, null), Is.True);
            Assert.That(ReferenceEquals(info.AvatarGameObject, null), Is.True);
        }

        [Test]
        public void ActorInfo_LiveOwnerAndAvatar_ResolveToTheSameObjects()
        {
            var go = new GameObject("actor-live");
            try
            {
                var info = new GameplayAbilityActorInfo(go, go);

                Assert.That(ReferenceEquals(info.OwnerUnityObject, go), Is.True);
                Assert.That(ReferenceEquals(info.AvatarGameObject, go), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ActorInfo_DestroyedAfterConstruction_ReadsAsNull()
        {
            var go = new GameObject("actor-late");
            var info = new GameplayAbilityActorInfo(go, go);
            Object.DestroyImmediate(go);

            Assert.That(ReferenceEquals(info.OwnerUnityObject, null), Is.True);
            Assert.That(ReferenceEquals(info.AvatarGameObject, null), Is.True);
        }

        [Test]
        public void AbilitySystemComponent_DestroyedAfterInit_ReadsAsNull()
        {
            var go = new GameObject("avatar-late");
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                asc.InitAbilityActorInfo(go, go);
                Object.DestroyImmediate(go);

                Assert.That(ReferenceEquals(asc.OwnerUnityObject, null), Is.True);
                Assert.That(ReferenceEquals(asc.AvatarGameObject, null), Is.True);
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void CueDispatch_AfterTargetAvatarDestroyed_PassesNullObject()
        {
            GameplayTagManager.InitializeIfNeeded();
            GameplayTagManager.RegisterDynamicTag("Test.GAS.Cue.Lifetime", "Cue lifetime test tag");
            GameplayTag cueTag = GameplayTagManager.Request("Test.GAS.Cue.Lifetime");
            var cueManager = new RecordingCueManager();
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority, cueManager: cueManager);
            var target = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var source = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var avatar = new GameObject("cue-target");
            try
            {
                target.InitAbilityActorInfo(new object(), avatar);

                var cues = new GameplayTagContainer();
                cues.AddTag(cueTag);
                var effect = new GameplayEffect("Lifetime.Cue", EDurationPolicy.Infinite, gameplayCues: cues);
                Assert.That(target.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, source)).Succeeded, Is.True);

                ActiveGameplayEffect active = target.FindActiveEffectByDef(effect);
                Object.DestroyImmediate(avatar);
                Assert.That(target.TryRemoveActiveEffect(active), Is.True);

                Assert.That(cueManager.Events.Count, Is.GreaterThan(0));
                (EGameplayCueEvent Event, GameplayCueEventParams Parameters) removed =
                    cueManager.Events[cueManager.Events.Count - 1];
                Assert.That(removed.Event, Is.EqualTo(EGameplayCueEvent.Removed));
                Assert.That(ReferenceEquals(removed.Parameters.TargetObject, null), Is.True,
                    "A destroyed target avatar must not reach cue handlers as a live object.");
            }
            finally
            {
                target.Dispose();
                source.Dispose();
                context.Dispose();
                if (avatar != null)
                {
                    Object.DestroyImmediate(avatar);
                }
            }
        }

        [Test]
        public void AbilitySystemComponent_DestroyedAvatar_ResolvesToNull()
        {
            var go = new GameObject("avatar");
            Object.DestroyImmediate(go);

            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            try
            {
                asc.InitAbilityActorInfo(new object(), go);

                Assert.That(ReferenceEquals(asc.AvatarGameObject, null), Is.True);
            }
            finally
            {
                asc.Dispose();
            }
        }

        private sealed class RecordingCueManager : IGameplayCueManager
        {
            public readonly List<(EGameplayCueEvent Event, GameplayCueEventParams Parameters)> Events =
                new List<(EGameplayCueEvent, GameplayCueEventParams)>();

            public void RegisterStaticCue(GameplayTag cueTag, string assetAddress)
            {
            }

            public void HandleCue(object asc, GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueEventParams parameters)
            {
                Events.Add((eventType, parameters));
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
