using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASNetworkExecutionTests
    {
        [Test]
        public void Replica_LocalPredictedActivationRequiresExplicitPredictionEntryPoint()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new NetworkTestAbility());

            Assert.That(asc.TryActivateAbility(spec), Is.False);
            Assert.That(asc.TryActivatePredictedAbility(spec, out GASPredictionKey predictionKey), Is.True);
            Assert.That(predictionKey.IsValid, Is.True);
            Assert.That(asc.HasOpenPredictionWindow(predictionKey), Is.True);
            Assert.That(spec.IsActive, Is.True);

            Assert.That(asc.RollbackPredictionWindow(predictionKey), Is.True);
            Assert.That(spec.IsActive, Is.False);

            asc.ClearAbility(spec);
            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void InputEdgesAreIdempotentAndIncludedInFullStateCapture()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new NetworkTestAbility());
            Assert.That(asc.TryExecuteAuthorityAbility(spec).Activated, Is.True);
            var ability = (NetworkTestAbility)spec.GetPrimaryInstance();

            Assert.That(asc.TrySetAbilityInputPressed(spec, true), Is.True);
            Assert.That(asc.TrySetAbilityInputPressed(spec, true), Is.True);
            Assert.That(asc.TrySetAbilityInputPressed(spec, false), Is.True);
            Assert.That(ability.PressedCount, Is.EqualTo(1));
            Assert.That(ability.ReleasedCount, Is.EqualTo(1));

            var state = new GASAbilitySystemFullStateBuffer();
            asc.CaptureFullStateNonAlloc(state);
            Assert.That(state.GrantedAbilityCount, Is.EqualTo(1));
            Assert.That(state.GrantedAbilities[0].IsInputPressed, Is.False);

            Assert.That(asc.TryCancelAbility(spec), Is.True);
            asc.ClearAbility(spec);
            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void AscCompletesCancellationWhenOverrideOmitsBaseCall()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new NonTerminatingCancelAbility());
            Assert.That(asc.TryExecuteAuthorityAbility(spec).Activated, Is.True);

            Assert.That(asc.TryCancelAbility(spec), Is.True);
            Assert.That(spec.IsActive, Is.False);
            Assert.That(((NonTerminatingCancelAbility)spec.GetPrimaryInstance()).CancelCount, Is.EqualTo(1));

            asc.ClearAbility(spec);
            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void AuthorityActivationPropagatesValidatedCommandCorrelation()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new NetworkTestAbility());
            var commandKey = new GASPredictionKey(17, asc.CoreEntity, 17);

            GASAuthorityActivationResult result = asc.TryExecuteAuthorityAbility(spec, commandKey);

            Assert.That(result.Activated, Is.True);
            Assert.That(((NetworkTestAbility)spec.GetPrimaryInstance()).ActivationPredictionKey, Is.EqualTo(commandKey));
            Assert.That(asc.TryCancelAbility(spec), Is.True);
            asc.ClearAbility(spec);
            asc.Dispose();
            context.Dispose();
        }

        [Test]
        public void ReplicaTracksAuthorityActiveStateWithoutExecutingGameplay()
        {
            var authorityContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var replicaContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var authority = new AbilitySystemComponent(authorityContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replica = new AbilitySystemComponent(replicaContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var definition = new NetworkTestAbility();
            GameplayAbilitySpec authoritySpec = authority.GrantAbility(definition);
            Assert.That(authority.TryExecuteAuthorityAbility(authoritySpec).Activated, Is.True);

            var activeState = new GASAbilitySystemStateDeltaBuffer();
            authority.CapturePendingStateDeltaNonAlloc(activeState);
            activeState.Sequence = 1u;

            Assert.That(replica.TryApplyStateDelta(activeState, out GASStateDeltaRejectionReason activeReason), Is.True);
            Assert.That(activeReason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(replica.AbilitySpecs.TryGetSpecByHandle(authoritySpec.Handle, out GameplayAbilitySpec replicaSpec), Is.True);
            var replicaAbility = (NetworkTestAbility)replicaSpec.GetPrimaryInstance();
            Assert.That(replicaSpec.IsActive, Is.True);
            Assert.That(replicaAbility.ActivationCount, Is.Zero);
            Assert.That(replica.TryCancelAbility(replicaSpec), Is.False);
            Assert.That(replicaAbility.CancelCount, Is.Zero);

            Assert.That(authority.TryCancelAbility(authoritySpec), Is.True);
            var inactiveState = new GASAbilitySystemStateDeltaBuffer();
            authority.CapturePendingStateDeltaNonAlloc(inactiveState);
            inactiveState.Sequence = 2u;

            Assert.That(replica.TryApplyStateDelta(inactiveState, out GASStateDeltaRejectionReason inactiveReason), Is.True);
            Assert.That(inactiveReason, Is.EqualTo(GASStateDeltaRejectionReason.None));
            Assert.That(replicaSpec.IsActive, Is.False);
            Assert.That(replicaAbility.ActivationCount, Is.Zero);
            Assert.That(replicaAbility.CancelCount, Is.Zero);

            authority.Dispose();
            replica.Dispose();
            authorityContext.Dispose();
            replicaContext.Dispose();
        }

        private sealed class NetworkTestAbility : GameplayAbility
        {
            public int PressedCount { get; private set; }
            public int ReleasedCount { get; private set; }
            public int ActivationCount { get; private set; }
            public int CancelCount { get; private set; }
            public GASPredictionKey ActivationPredictionKey { get; private set; }

            public NetworkTestAbility()
            {
                Initialize(
                    "NetworkTestAbility",
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    EAbilityExecutionPolicy.LocalPredicted,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            public override GameplayAbility CreateRuntimeInstance() => new NetworkTestAbility();

            public override void ActivateAbility(
                GameplayAbilityActorInfo actorInfo,
                GameplayAbilitySpec spec,
                GameplayAbilityActivationInfo activationInfo)
            {
                ActivationCount++;
                ActivationPredictionKey = activationInfo.PredictionKey;
            }

            public override void CancelAbility()
            {
                CancelCount++;
                base.CancelAbility();
            }

            public override void InputPressed(GameplayAbilitySpec spec) => PressedCount++;

            public override void InputReleased(GameplayAbilitySpec spec) => ReleasedCount++;

        }

        private sealed class NonTerminatingCancelAbility : GameplayAbility
        {
            public int CancelCount { get; private set; }

            public NonTerminatingCancelAbility()
            {
                Initialize(
                    "NonTerminatingCancelAbility",
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    EAbilityExecutionPolicy.LocalPredicted,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            public override GameplayAbility CreateRuntimeInstance() => new NonTerminatingCancelAbility();

            public override void CancelAbility() => CancelCount++;
        }
    }
}
