using System;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASTerminalContractAndVersionGuardTests
    {
        [TestCase(true, 1, 0)]
        [TestCase(false, 0, 1)]
        public void WaitTargetData_FirstTerminalSignalWins(
            bool completionFirst,
            int expectedCompletionCount,
            int expectedCancellationCount)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            var actor = new DoubleTerminalTargetActor(completionFirst);
            AbilityTask_WaitTargetData task = AbilityTask_WaitTargetData.WaitTargetData(
                spec.GetPrimaryInstance(),
                actor);
            int completionCount = 0;
            int cancellationCount = 0;
            task.OnValidData = _ => completionCount++;
            task.OnCancelled = () => cancellationCount++;

            task.Activate();

            Assert.That(completionCount, Is.EqualTo(expectedCompletionCount));
            Assert.That(cancellationCount, Is.EqualTo(expectedCancellationCount));
            Assert.That(actor.ConfigureCount, Is.EqualTo(1));
            Assert.That(actor.StartCount, Is.EqualTo(1));
            Assert.That(actor.DestroyCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WaitConfirmCancel_ThrowingTerminalCallbackStillTearsDown(bool confirm)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            AbilityTask_WaitConfirmCancel task = AbilityTask_WaitConfirmCancel.WaitConfirmCancel(
                spec.GetPrimaryInstance());
            int callbackCount = 0;
            Action throwingCallback = () =>
            {
                callbackCount++;
                throw new InvalidOperationException("Expected terminal callback failure.");
            };
            if (confirm)
            {
                task.OnConfirm = throwingCallback;
            }
            else
            {
                task.OnCancel = throwingCallback;
            }
            task.Activate();

            Assert.Throws<InvalidOperationException>(() =>
            {
                if (confirm)
                {
                    task.Confirm();
                }
                else
                {
                    task.Cancel();
                }
            });

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void WaitTargetData_ThrowingCancelCallbackStillTearsDown()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            var actor = new DoubleTerminalTargetActor(completionFirst: false);
            AbilityTask_WaitTargetData task = AbilityTask_WaitTargetData.WaitTargetData(
                spec.GetPrimaryInstance(),
                actor);
            int callbackCount = 0;
            task.OnCancelled = () =>
            {
                callbackCount++;
                throw new InvalidOperationException("Expected target cancellation callback failure.");
            };

            Assert.Throws<InvalidOperationException>(task.Activate);

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(actor.DestroyCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void WaitTargetData_TargetDataReturnFailureStillTearsDown()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            var actor = new CompletingTargetActor(new ThrowingResetTargetData());
            AbilityTask_WaitTargetData task = AbilityTask_WaitTargetData.WaitTargetData(
                spec.GetPrimaryInstance(),
                actor);
            int callbackCount = 0;
            task.OnValidData = _ => callbackCount++;

            Assert.Throws<InvalidOperationException>(task.Activate);

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(actor.DestroyCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void TickTasks_EndingEarlierSiblingTicksEachInitialTaskAtMostOnce()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            LifecycleTickTask first = CreateTickTask(ability);
            LifecycleTickTask second = CreateTickTask(ability);
            LifecycleTickTask third = CreateTickTask(ability);
            int firstCount = 0;
            int secondCount = 0;
            int thirdCount = 0;
            first.Callback = () => firstCount++;
            second.Callback = () => secondCount++;
            third.Callback = () =>
            {
                thirdCount++;
                second.EndTask();
            };

            ability.TickTasks(0f);

            Assert.That(firstCount, Is.EqualTo(1));
            Assert.That(secondCount, Is.Zero);
            Assert.That(thirdCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.EqualTo(2));

            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void TickTasks_EndAbilityInsideCallbackStopsTraversalAndTearsDownTasks()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            Assert.That(asc.TryActivateAbility(spec), Is.True);
            GameplayAbility ability = spec.GetPrimaryInstance();
            LifecycleTickTask first = CreateTickTask(ability);
            LifecycleTickTask second = CreateTickTask(ability);
            LifecycleTickTask third = CreateTickTask(ability);
            int firstCount = 0;
            int secondCount = 0;
            int thirdCount = 0;
            first.Callback = () => firstCount++;
            second.Callback = () => secondCount++;
            third.Callback = () =>
            {
                thirdCount++;
                ability.EndAbility();
            };

            Assert.DoesNotThrow(() => ability.TickTasks(0f));

            Assert.That(firstCount, Is.Zero);
            Assert.That(secondCount, Is.Zero);
            Assert.That(thirdCount, Is.EqualTo(1));
            Assert.That(spec.IsActive, Is.False);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);
            Assert.That(asc.ValidateRuntimeIndexes(), Is.True);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [TestCase("AbilityActivated")]
        [TestCase("AbilityEnded")]
        [TestCase("AttributeChanged")]
        [TestCase("EffectApplied")]
        [TestCase("EffectRemoved")]
        [TestCase("GameplayEvent")]
        [TestCase("TagAdded")]
        [TestCase("TagRemoved")]
        public void SubscribedOneShotTask_ThrowingSuccessCallbackStillTearsDown(string taskKind)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec ownerSpec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ownerAbility = ownerSpec.GetPrimaryInstance();
            AbilityTask task;
            Action trigger;
            int callbackCount = 0;
            Action throwFromCallback = () =>
            {
                callbackCount++;
                throw new InvalidOperationException("Expected terminal callback failure.");
            };

            switch (taskKind)
            {
                case "AbilityActivated":
                {
                    GameplayAbilitySpec observedSpec = asc.GrantAbility(new TargetingTestAbility());
                    var typedTask = AbilityTask_WaitAbilityActivate.WaitAbilityActivate(ownerAbility);
                    typedTask.OnAbilityActivated = _ => throwFromCallback();
                    task = typedTask;
                    trigger = () => asc.TryActivateAbility(observedSpec);
                    break;
                }
                case "AbilityEnded":
                {
                    GameplayAbilitySpec observedSpec = asc.GrantAbility(new TargetingTestAbility());
                    Assert.That(asc.TryActivateAbility(observedSpec), Is.True);
                    var typedTask = AbilityTask_WaitAbilityEnd.WaitAbilityEnd(ownerAbility);
                    typedTask.OnAbilityEnded = _ => throwFromCallback();
                    task = typedTask;
                    trigger = () => observedSpec.GetPrimaryInstance().EndAbility();
                    break;
                }
                case "AttributeChanged":
                {
                    var attributes = new TerminalAttributeSet();
                    asc.AddAttributeSet(attributes);
                    var typedTask = AbilityTask_WaitAttributeChange.WaitAttributeChange(
                        ownerAbility,
                        attributes.Value.Name);
                    typedTask.OnAttributeChanged = (_, __) => throwFromCallback();
                    task = typedTask;
                    trigger = () => attributes.Value.SetCurrentValue(1f);
                    break;
                }
                case "EffectApplied":
                {
                    var typedTask = AbilityTask_WaitGameplayEffectApplied.WaitGameplayEffectApplied(ownerAbility);
                    typedTask.OnEffectApplied = _ => throwFromCallback();
                    task = typedTask;
                    trigger = () => asc.ApplyGameplayEffectSpecToSelf(
                        GameplayEffectSpec.Create(CreateTerminalEffect("Applied"), asc));
                    break;
                }
                case "EffectRemoved":
                {
                    GameplayEffectApplicationResult application = asc.ApplyGameplayEffectSpecToSelf(
                        GameplayEffectSpec.Create(CreateTerminalEffect("Removed"), asc));
                    Assert.That(application.Succeeded, Is.True);
                    var typedTask = AbilityTask_WaitGameplayEffectRemoved.WaitGameplayEffectRemoved(ownerAbility);
                    typedTask.OnEffectRemoved = _ => throwFromCallback();
                    task = typedTask;
                    trigger = () => asc.TryRemoveActiveEffect(application.ActiveEffect);
                    break;
                }
                case "GameplayEvent":
                {
                    GameplayTag tag = RegisterTag("Test.GAS.Terminal.GameplayEvent");
                    var typedTask = AbilityTask_WaitGameplayEvent.WaitGameplayEvent(ownerAbility, tag);
                    typedTask.OnEventReceived = _ => throwFromCallback();
                    task = typedTask;
                    trigger = () => asc.HandleGameplayEvent(new GameplayEventData { EventTag = tag, Target = asc });
                    break;
                }
                case "TagAdded":
                {
                    GameplayTag tag = RegisterTag("Test.GAS.Terminal.TagAdded");
                    var typedTask = AbilityTask_WaitGameplayTagAdded.WaitGameplayTagAdded(ownerAbility, tag);
                    typedTask.OnTagAdded = throwFromCallback;
                    task = typedTask;
                    trigger = () => asc.AddLooseGameplayTag(tag);
                    break;
                }
                case "TagRemoved":
                {
                    GameplayTag tag = RegisterTag("Test.GAS.Terminal.TagRemoved");
                    asc.AddLooseGameplayTag(tag);
                    var typedTask = AbilityTask_WaitGameplayTagRemoved.WaitGameplayTagRemoved(ownerAbility, tag);
                    typedTask.OnTagRemoved = throwFromCallback;
                    task = typedTask;
                    trigger = () => asc.RemoveLooseGameplayTag(tag);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(taskKind), taskKind, "Unknown terminal task kind.");
            }

            task.Activate();
            InvokeAllowingExpectedTerminalFailure(trigger);

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.Dispose();
        }

        [TestCase("Delay")]
        [TestCase("RepeatFinished")]
        [TestCase("RepeatAction")]
        public void TickTask_ThrowingCallbackStillTearsDown(string taskKind)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            int callbackCount = 0;
            Action throwFromCallback = () =>
            {
                callbackCount++;
                throw new InvalidOperationException("Expected terminal callback failure.");
            };
            Action trigger;

            switch (taskKind)
            {
                case "Delay":
                {
                    AbilityTask_WaitDelay task = AbilityTask_WaitDelay.WaitDelay(ability, 0f);
                    task.OnFinishDelay = throwFromCallback;
                    trigger = task.Activate;
                    break;
                }
                case "RepeatFinished":
                {
                    AbilityTask_Repeat task = AbilityTask_Repeat.Repeat(ability, 0.1f, 0);
                    task.OnFinished = throwFromCallback;
                    trigger = task.Activate;
                    break;
                }
                case "RepeatAction":
                {
                    AbilityTask_Repeat task = AbilityTask_Repeat.Repeat(ability, 0.1f, -1);
                    task.OnPerformAction = _ =>
                    {
                        throwFromCallback();
                        return true;
                    };
                    task.Activate();
                    trigger = () => task.Tick(1f);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(taskKind), taskKind, "Unknown ticking task kind.");
            }

            InvokeAllowingExpectedTerminalFailure(trigger);

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void WaitTargetData_ThrowingValidDataCallbackStillTearsDown()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            var actor = new CompletingTargetActor(null);
            AbilityTask_WaitTargetData task = AbilityTask_WaitTargetData.WaitTargetData(
                spec.GetPrimaryInstance(),
                actor);
            int callbackCount = 0;
            task.OnValidData = _ =>
            {
                callbackCount++;
                throw new InvalidOperationException("Expected terminal callback failure.");
            };

            Assert.Throws<InvalidOperationException>(task.Activate);

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(actor.DestroyCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [TestCase("AbilityActivated")]
        [TestCase("AbilityEnded")]
        [TestCase("AttributeChanged")]
        [TestCase("EffectApplied")]
        [TestCase("EffectRemoved")]
        [TestCase("GameplayEvent")]
        [TestCase("TagAdded")]
        [TestCase("TagRemoved")]
        [TestCase("Repeat")]
        [TestCase("ConfirmCancel")]
        [TestCase("TargetData")]
        public void CancelTask_ThrowingCallbackStillTearsDown(string taskKind)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            int callbackCount = 0;
            Action throwFromCallback = () =>
            {
                callbackCount++;
                throw new InvalidOperationException("Expected cancellation callback failure.");
            };
            AbilityTask task;

            switch (taskKind)
            {
                case "AbilityActivated":
                {
                    var typedTask = AbilityTask_WaitAbilityActivate.WaitAbilityActivate(ability);
                    typedTask.OnCancelled = throwFromCallback;
                    task = typedTask;
                    break;
                }
                case "AbilityEnded":
                {
                    var typedTask = AbilityTask_WaitAbilityEnd.WaitAbilityEnd(ability);
                    typedTask.OnCancelled = throwFromCallback;
                    task = typedTask;
                    break;
                }
                case "AttributeChanged":
                {
                    var attributes = new TerminalAttributeSet();
                    asc.AddAttributeSet(attributes);
                    var typedTask = AbilityTask_WaitAttributeChange.WaitAttributeChange(ability, attributes.Value.Name);
                    typedTask.OnCancelled = throwFromCallback;
                    task = typedTask;
                    break;
                }
                case "EffectApplied":
                {
                    var typedTask = AbilityTask_WaitGameplayEffectApplied.WaitGameplayEffectApplied(ability);
                    typedTask.OnCancelled = throwFromCallback;
                    task = typedTask;
                    break;
                }
                case "EffectRemoved":
                {
                    var typedTask = AbilityTask_WaitGameplayEffectRemoved.WaitGameplayEffectRemoved(ability);
                    typedTask.OnCancelled = throwFromCallback;
                    task = typedTask;
                    break;
                }
                case "GameplayEvent":
                {
                    GameplayTag tag = RegisterTag("Test.GAS.Terminal.CancelGameplayEvent");
                    var typedTask = AbilityTask_WaitGameplayEvent.WaitGameplayEvent(ability, tag);
                    typedTask.OnCancelled = throwFromCallback;
                    task = typedTask;
                    break;
                }
                case "TagAdded":
                {
                    GameplayTag tag = RegisterTag("Test.GAS.Terminal.CancelTagAdded");
                    var typedTask = AbilityTask_WaitGameplayTagAdded.WaitGameplayTagAdded(ability, tag);
                    typedTask.OnCancelled = throwFromCallback;
                    task = typedTask;
                    break;
                }
                case "TagRemoved":
                {
                    GameplayTag tag = RegisterTag("Test.GAS.Terminal.CancelTagRemoved");
                    asc.AddLooseGameplayTag(tag);
                    var typedTask = AbilityTask_WaitGameplayTagRemoved.WaitGameplayTagRemoved(ability, tag);
                    typedTask.OnCancelled = throwFromCallback;
                    task = typedTask;
                    break;
                }
                case "Repeat":
                {
                    AbilityTask_Repeat typedTask = AbilityTask_Repeat.Repeat(ability, 1f, -1);
                    typedTask.OnCancelled = throwFromCallback;
                    task = typedTask;
                    break;
                }
                case "ConfirmCancel":
                {
                    AbilityTask_WaitConfirmCancel typedTask = AbilityTask_WaitConfirmCancel.WaitConfirmCancel(ability);
                    typedTask.OnCancel = throwFromCallback;
                    task = typedTask;
                    break;
                }
                case "TargetData":
                {
                    AbilityTask_WaitTargetData typedTask = AbilityTask_WaitTargetData.WaitTargetData(
                        ability,
                        new PassiveTargetActor());
                    typedTask.OnCancelled = throwFromCallback;
                    task = typedTask;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(taskKind), taskKind, "Unknown cancellation task kind.");
            }

            task.Activate();
            Assert.Throws<InvalidOperationException>(task.CancelTask);

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void EndAbility_CancelCallbackCannotCreateOrLeakTask()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            AbilityTask_WaitConfirmCancel task = AbilityTask_WaitConfirmCancel.WaitConfirmCancel(ability);
            int callbackCount = 0;
            task.OnCancel = () =>
            {
                callbackCount++;
                Assert.Throws<InvalidOperationException>(() => ability.NewAbilityTask<LifecycleTickTask>());
            };
            task.Activate();

            Assert.DoesNotThrow(ability.EndAbility);

            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void OneShotSuccessCallback_CannotPublishCancellationReentrantly()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayTag tag = RegisterTag("Test.GAS.Terminal.ReentrantSuccess");
            AbilityTask_WaitGameplayEvent task = AbilityTask_WaitGameplayEvent.WaitGameplayEvent(
                spec.GetPrimaryInstance(),
                tag);
            int completionCount = 0;
            int cancellationCount = 0;
            task.OnEventReceived = _ =>
            {
                completionCount++;
                task.CancelTask();
            };
            task.OnCancelled = () => cancellationCount++;
            task.Activate();

            asc.HandleGameplayEvent(new GameplayEventData { EventTag = tag, Target = asc });

            Assert.That(completionCount, Is.EqualTo(1));
            Assert.That(cancellationCount, Is.Zero);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void CancelCallback_CannotReenterOrPublishTwice()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayTag tag = RegisterTag("Test.GAS.Terminal.ReentrantCancel");
            AbilityTask_WaitGameplayEvent task = AbilityTask_WaitGameplayEvent.WaitGameplayEvent(
                spec.GetPrimaryInstance(),
                tag);
            int cancellationCount = 0;
            task.OnCancelled = () =>
            {
                cancellationCount++;
                task.CancelTask();
            };
            task.Activate();

            Assert.DoesNotThrow(task.CancelTask);

            Assert.That(cancellationCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.ClearAbility(spec);
            asc.Dispose();
        }

        [Test]
        public void EndAbility_ThrowingCancellationCallbacksDestroyEveryTask()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            GameplayTag eventTag = RegisterTag("Test.GAS.Terminal.EndCleanupEvent");
            GameplayTag tag = RegisterTag("Test.GAS.Terminal.EndCleanupTag");
            AbilityTask_WaitGameplayEvent eventTask = AbilityTask_WaitGameplayEvent.WaitGameplayEvent(ability, eventTag);
            AbilityTask_WaitGameplayTagAdded tagTask = AbilityTask_WaitGameplayTagAdded.WaitGameplayTagAdded(ability, tag);
            AbilityTask_Repeat repeatTask = AbilityTask_Repeat.Repeat(ability, 1f, -1);
            int cancellationCount = 0;
            Action throwingCancellation = () =>
            {
                cancellationCount++;
                throw new InvalidOperationException("Expected cancellation callback failure.");
            };
            eventTask.OnCancelled = throwingCancellation;
            tagTask.OnCancelled = throwingCancellation;
            repeatTask.OnCancelled = throwingCancellation;
            eventTask.Activate();
            tagTask.Activate();
            repeatTask.Activate();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(ability.EndAbility);

            Assert.That(exception.Message, Does.Contain("Expected cancellation callback failure"));
            Assert.That(cancellationCount, Is.EqualTo(3));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);

            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void TerminalCallback_EndAndCreateReplacementKeepsReferencesIsolated()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            AbilityTask_WaitConfirmCancel task = AbilityTask_WaitConfirmCancel.WaitConfirmCancel(ability);
            AbilityTask_WaitConfirmCancel replacement = null;
            task.OnConfirm = () =>
            {
                task.EndTask();
                replacement = AbilityTask_WaitConfirmCancel.WaitConfirmCancel(ability);
                replacement.Activate();
            };
            task.Activate();

            task.Confirm();

            Assert.That(replacement, Is.Not.SameAs(task));
            Assert.That(replacement.IsActive, Is.True);
            Assert.DoesNotThrow(task.EndTask);
            Assert.That(replacement.IsActive, Is.True, "A completed task reference must not end its replacement.");
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.EqualTo(1));

            replacement.EndTask();
            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void ActivateCatch_EndAndCreateReplacementKeepsReferencesIsolated()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            ReentrantActivateTask task = ability.NewAbilityTask<ReentrantActivateTask>();
            ReentrantActivateTask replacement = null;
            task.Callback = () =>
            {
                task.EndTask();
                replacement = ability.NewAbilityTask<ReentrantActivateTask>();
                replacement.Activate();
                throw new InvalidOperationException("Expected activation callback failure.");
            };

            Assert.Throws<InvalidOperationException>(task.Activate);

            Assert.That(replacement, Is.Not.SameAs(task));
            Assert.That(replacement.IsActive, Is.True);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.EqualTo(1));

            replacement.EndTask();
            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void TickCatch_EndAndCreateReplacementKeepsReferencesIsolated()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            LifecycleTickTask task = CreateTickTask(ability);
            LifecycleTickTask replacement = null;
            task.Callback = () =>
            {
                task.EndTask();
                replacement = CreateTickTask(ability);
                throw new InvalidOperationException("Expected tick callback failure.");
            };

            Assert.DoesNotThrow(() => ability.TickTasks(0f));

            Assert.That(replacement, Is.Not.SameAs(task));
            Assert.That(replacement.IsActive, Is.True);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.EqualTo(1));

            replacement.EndTask();
            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void PredictionCancellation_SnapshotSkipsTaskCreatedDuringCallback()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            var predictionKey = new GASPredictionKey(701);
            ability.SetCurrentActivationInfo(new GameplayAbilityActivationInfo { PredictionKey = predictionKey });
            AbilityTask_WaitConfirmCancel first = AbilityTask_WaitConfirmCancel.WaitConfirmCancel(ability);
            AbilityTask_WaitConfirmCancel second = AbilityTask_WaitConfirmCancel.WaitConfirmCancel(ability);
            AbilityTask_WaitConfirmCancel third = AbilityTask_WaitConfirmCancel.WaitConfirmCancel(ability);
            AbilityTask_WaitConfirmCancel replacement = null;
            int secondCancellationCount = 0;
            int thirdCancellationCount = 0;
            int replacementCancellationCount = 0;
            second.OnCancel = () => secondCancellationCount++;
            third.OnCancel = () =>
            {
                thirdCancellationCount++;
                first.EndTask();
                replacement = AbilityTask_WaitConfirmCancel.WaitConfirmCancel(ability);
                replacement.OnCancel = () => replacementCancellationCount++;
                replacement.Activate();
            };
            first.Activate();
            second.Activate();
            third.Activate();

            ability.RollbackTasksForPredictionKey(predictionKey);

            Assert.That(replacement, Is.Not.SameAs(first));
            Assert.That(secondCancellationCount, Is.EqualTo(1));
            Assert.That(thirdCancellationCount, Is.EqualTo(1));
            Assert.That(replacementCancellationCount, Is.Zero);
            Assert.That(replacement.IsActive, Is.True);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.EqualTo(1));

            replacement.EndTask();
            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void AbilityTask_EndTaskReentrancyDestroysLeaseOnce()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            ReentrantDestroyTask task = spec.GetPrimaryInstance().NewAbilityTask<ReentrantDestroyTask>();
            task.Activate();

            Assert.DoesNotThrow(task.EndTask);

            Assert.That(task.DestroyCount, Is.EqualTo(1));
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);
            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void NewAbilityTask_ReentrantInitializationKeepsReplacementIdentityIsolated()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            ReentrantInitTask.ResetTestState();
            ReentrantInitTask.ReenterOnNextInitialization = true;

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => ability.NewAbilityTask<ReentrantInitTask>());

            ReentrantInitTask replacement = ReentrantInitTask.Replacement;
            Assert.That(replacement, Is.Not.SameAs(ReentrantInitTask.OriginalLease));
            Assert.That(replacement.IsActive, Is.False);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.EqualTo(1));

            replacement.EndTask();
            ReentrantInitTask.ResetTestState();
            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void WaitGameplayTagAdded_ImmediateContinuousReplacementDoesNotDuplicateOrLeakSubscription()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            GameplayTag watchedTag = RegisterTag("Test.GAS.Terminal.TagAddedImmediateReplacement");
            GameplayTag probeTag = RegisterTag("Test.GAS.Terminal.TagAddedImmediateReplacementProbe");
            asc.AddLooseGameplayTag(watchedTag);
            AbilityTask_WaitGameplayTagAdded task = AbilityTask_WaitGameplayTagAdded.WaitGameplayTagAdded(
                ability,
                watchedTag,
                triggerOnce: false);
            AbilityTask_WaitGameplayTagAdded replacement = null;
            int originalCallbackCount = 0;
            int replacementCallbackCount = 0;
            task.OnTagAdded = () =>
            {
                originalCallbackCount++;
                task.EndTask();
                replacement = AbilityTask_WaitGameplayTagAdded.WaitGameplayTagAdded(
                    ability,
                    watchedTag,
                    triggerOnce: false);
                replacement.OnTagAdded = () => replacementCallbackCount++;
                replacement.Activate();
            };

            task.Activate();

            Assert.That(replacement, Is.Not.SameAs(task));
            Assert.That(originalCallbackCount, Is.EqualTo(1));
            Assert.That(replacementCallbackCount, Is.EqualTo(1));
            asc.RemoveLooseGameplayTag(watchedTag);
            asc.AddLooseGameplayTag(watchedTag);
            Assert.That(replacementCallbackCount, Is.EqualTo(2));

            replacement.EndTask();
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);
            AbilityTask_WaitGameplayTagAdded probe = AbilityTask_WaitGameplayTagAdded.WaitGameplayTagAdded(
                ability,
                probeTag,
                triggerOnce: false);
            int probeCallbackCount = 0;
            probe.OnTagAdded = () => probeCallbackCount++;
            probe.Activate();
            Assert.That(probe, Is.Not.SameAs(replacement));

            asc.RemoveLooseGameplayTag(watchedTag);
            asc.AddLooseGameplayTag(watchedTag);
            Assert.That(probeCallbackCount, Is.Zero);
            asc.AddLooseGameplayTag(probeTag);
            Assert.That(probeCallbackCount, Is.EqualTo(1));

            probe.EndTask();
            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void WaitGameplayTagRemoved_ImmediateContinuousReplacementDoesNotDuplicateOrLeakSubscription()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayAbilitySpec spec = asc.GrantAbility(new TargetingTestAbility());
            GameplayAbility ability = spec.GetPrimaryInstance();
            GameplayTag watchedTag = RegisterTag("Test.GAS.Terminal.TagRemovedImmediateReplacement");
            GameplayTag probeTag = RegisterTag("Test.GAS.Terminal.TagRemovedImmediateReplacementProbe");
            AbilityTask_WaitGameplayTagRemoved task = AbilityTask_WaitGameplayTagRemoved.WaitGameplayTagRemoved(
                ability,
                watchedTag,
                triggerOnce: false);
            AbilityTask_WaitGameplayTagRemoved replacement = null;
            int originalCallbackCount = 0;
            int replacementCallbackCount = 0;
            task.OnTagRemoved = () =>
            {
                originalCallbackCount++;
                task.EndTask();
                replacement = AbilityTask_WaitGameplayTagRemoved.WaitGameplayTagRemoved(
                    ability,
                    watchedTag,
                    triggerOnce: false);
                replacement.OnTagRemoved = () => replacementCallbackCount++;
                replacement.Activate();
            };

            task.Activate();

            Assert.That(replacement, Is.Not.SameAs(task));
            Assert.That(originalCallbackCount, Is.EqualTo(1));
            Assert.That(replacementCallbackCount, Is.EqualTo(1));
            asc.AddLooseGameplayTag(watchedTag);
            asc.RemoveLooseGameplayTag(watchedTag);
            Assert.That(replacementCallbackCount, Is.EqualTo(2));

            replacement.EndTask();
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().Tasks.Active, Is.Zero);
            asc.AddLooseGameplayTag(probeTag);
            AbilityTask_WaitGameplayTagRemoved probe = AbilityTask_WaitGameplayTagRemoved.WaitGameplayTagRemoved(
                ability,
                probeTag,
                triggerOnce: false);
            int probeCallbackCount = 0;
            probe.OnTagRemoved = () => probeCallbackCount++;
            probe.Activate();
            Assert.That(probe, Is.Not.SameAs(replacement));

            asc.AddLooseGameplayTag(watchedTag);
            asc.RemoveLooseGameplayTag(watchedTag);
            Assert.That(probeCallbackCount, Is.Zero);
            asc.RemoveLooseGameplayTag(probeTag);
            Assert.That(probeCallbackCount, Is.EqualTo(1));

            probe.EndTask();
            asc.ClearAbility(spec);
            Assert.That(asc.RuntimeContext.GetMemoryStatistics().OutstandingLeases, Is.Zero);
            asc.Dispose();
        }

        [Test]
        public void ReplicationStateBuilder_StateVersionExhaustionThrowsWithoutWrapping()
        {
            var builder = new ReplicationStateBuilder
            {
                StateVersion = ulong.MaxValue
            };

            Assert.Throws<InvalidOperationException>(() => builder.MarkActiveEffectsDirty());
            Assert.That(builder.StateVersion, Is.EqualTo(ulong.MaxValue));
            Assert.That(builder.ActiveEffectsDirty, Is.False);
        }

        [Test]
        public void ReplicationStateBuilder_StateVersionExhaustionPreventsAttributeRevisionMutation()
        {
            var builder = new ReplicationStateBuilder
            {
                StateVersion = ulong.MaxValue,
                AttributeRegistryVersion = 17U
            };

            Assert.Throws<InvalidOperationException>(() => builder.MarkAttributeStructureDirty());
            Assert.That(builder.StateVersion, Is.EqualTo(ulong.MaxValue));
            Assert.That(builder.AttributeRegistryVersion, Is.EqualTo(17U));
            Assert.That(builder.AttributeStructureDirty, Is.False);
        }

        [Test]
        public void ReplicationStateBuilder_AttributeRegistryRevisionExhaustionThrowsWithoutWrapping()
        {
            var builder = new ReplicationStateBuilder
            {
                StateVersion = 41UL,
                AttributeRegistryVersion = uint.MaxValue
            };

            Assert.Throws<InvalidOperationException>(() => builder.MarkAttributeStructureDirty());
            Assert.That(builder.AttributeRegistryVersion, Is.EqualTo(uint.MaxValue));
            Assert.That(builder.StateVersion, Is.EqualTo(41UL));
            Assert.That(builder.AttributeStructureDirty, Is.False);
        }

        private static void InvokeAllowingExpectedTerminalFailure(Action action)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException exception)
            {
                Assert.That(exception.Message, Does.Contain("Expected terminal callback failure"));
            }
        }

        private static GameplayEffect CreateTerminalEffect(string suffix)
        {
            return new GameplayEffect(
                $"Terminal{suffix}",
                EDurationPolicy.Infinite);
        }

        private static GameplayTag RegisterTag(string name)
        {
            GameplayTagManager.RegisterDynamicTag(name, "Gameplay Ability terminal contract test tag");
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.RequestTag(name);
        }

        private sealed class TerminalAttributeSet : AttributeSet
        {
            public GameplayAttribute Value { get; } = new GameplayAttribute("TerminalValue");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Value);
            }
        }

        private sealed class TargetingTestAbility : GameplayAbility
        {
            public TargetingTestAbility()
            {
                Initialize(
                    "TargetingTest",
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    EAbilityExecutionPolicy.LocalOnly,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new TargetingTestAbility();
            }
        }

        private static LifecycleTickTask CreateTickTask(GameplayAbility ability)
        {
            LifecycleTickTask task = ability.NewAbilityTask<LifecycleTickTask>();
            task.Activate();
            return task;
        }

        private sealed class LifecycleTickTask : AbilityTask, IAbilityTaskTick
        {
            public LifecycleTickTask()
            {
            }

            public Action Callback { get; set; }

            public void Tick(float deltaTime)
            {
                Callback?.Invoke();
            }

            protected override void OnActivate()
            {
            }

            protected override void OnDestroy()
            {
                Callback = null;
                base.OnDestroy();
            }
        }

        private sealed class ReentrantActivateTask : AbilityTask
        {
            public Action Callback { get; set; }

            protected override void OnActivate()
            {
                Callback?.Invoke();
            }

            protected override void OnDestroy()
            {
                Callback = null;
                base.OnDestroy();
            }
        }

        private sealed class ReentrantDestroyTask : AbilityTask
        {
            public int DestroyCount { get; private set; }

            protected override void OnActivate()
            {
            }

            protected override void OnDestroy()
            {
                DestroyCount++;
                EndTask();
                base.OnDestroy();
            }
        }

        private sealed class ReentrantInitTask : AbilityTask
        {
            public static bool ReenterOnNextInitialization { get; set; }
            public static ReentrantInitTask OriginalLease { get; private set; }
            public static ReentrantInitTask Replacement { get; private set; }

            public static void ResetTestState()
            {
                ReenterOnNextInitialization = false;
                OriginalLease = null;
                Replacement = null;
            }

            public override void InitTask(GameplayAbility ability)
            {
                base.InitTask(ability);
                if (!ReenterOnNextInitialization)
                {
                    return;
                }

                ReenterOnNextInitialization = false;
                OriginalLease = this;
                EndTask();
                Replacement = ability.NewAbilityTask<ReentrantInitTask>();
            }

            protected override void OnActivate()
            {
            }
        }

        private sealed class ThrowingResetTargetData : TargetData
        {
            internal override void ResetRuntimeState()
            {
                throw new InvalidOperationException("Expected target data release failure.");
            }
        }

        private sealed class CompletingTargetActor : ITargetActor
        {
            private readonly TargetData data;
            private Action<TargetData> onTargetDataReady;
            private Action onCancelled;

            public CompletingTargetActor(TargetData data)
            {
                this.data = data;
            }

            public int DestroyCount { get; private set; }

            public void Configure(
                GameplayAbility ability,
                Action<TargetData> onTargetDataReady,
                Action onCancelled)
            {
                this.onTargetDataReady = onTargetDataReady;
                this.onCancelled = onCancelled;
            }

            public void StartTargeting()
            {
                onTargetDataReady?.Invoke(data);
            }

            public void ConfirmTargeting()
            {
            }

            public void CancelTargeting()
            {
                onCancelled?.Invoke();
            }

            public void Destroy()
            {
                DestroyCount++;
                onTargetDataReady = null;
                onCancelled = null;
            }
        }

        private sealed class PassiveTargetActor : ITargetActor
        {
            private Action<TargetData> onTargetDataReady;
            private Action onCancelled;

            public void Configure(
                GameplayAbility ability,
                Action<TargetData> onTargetDataReady,
                Action onCancelled)
            {
                this.onTargetDataReady = onTargetDataReady;
                this.onCancelled = onCancelled;
            }

            public void StartTargeting()
            {
            }

            public void ConfirmTargeting()
            {
                onTargetDataReady?.Invoke(null);
            }

            public void CancelTargeting()
            {
                onCancelled?.Invoke();
            }

            public void Destroy()
            {
                onTargetDataReady = null;
                onCancelled = null;
            }
        }

        private sealed class DoubleTerminalTargetActor : ITargetActor
        {
            private readonly bool completionFirst;
            private Action<TargetData> onTargetDataReady;
            private Action onCancelled;

            public DoubleTerminalTargetActor(bool completionFirst)
            {
                this.completionFirst = completionFirst;
            }

            public int ConfigureCount { get; private set; }
            public int StartCount { get; private set; }
            public int DestroyCount { get; private set; }

            public void Configure(
                GameplayAbility ability,
                Action<TargetData> onTargetDataReady,
                Action onCancelled)
            {
                ConfigureCount++;
                this.onTargetDataReady = onTargetDataReady;
                this.onCancelled = onCancelled;
            }

            public void StartTargeting()
            {
                StartCount++;
                Action<TargetData> completion = onTargetDataReady;
                Action cancellation = onCancelled;
                if (completionFirst)
                {
                    completion?.Invoke(null);
                    cancellation?.Invoke();
                }
                else
                {
                    cancellation?.Invoke();
                    completion?.Invoke(null);
                }
            }

            public void ConfirmTargeting()
            {
            }

            public void CancelTargeting()
            {
            }

            public void Destroy()
            {
                DestroyCount++;
                onTargetDataReady = null;
                onCancelled = null;
            }
        }
    }
}
