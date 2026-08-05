// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioActionEventCancellationTests
    {
        [Test]
        public void Execute_WithPreCanceledToken_DoesNotRunImmediateAction()
        {
            AudioStateGroup stateGroup = CreateStateGroup();
            var action = CreateSetStateAction(stateGroup, delaySeconds: 0f, stateValue: 1);
            AudioActionEvent actionEvent = CreateActionEvent(action);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            try
            {
                Assert.Throws<OperationCanceledException>(() =>
                    actionEvent.Execute((GameObject)null, cancellation.Token));
                Assert.AreEqual(0, stateGroup.CurrentValue);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actionEvent);
                UnityEngine.Object.DestroyImmediate(stateGroup);
            }
        }

        [UnityTest]
        public IEnumerator Execute_CancelDuringDelay_DoesNotRunAction()
        {
            AudioStateGroup stateGroup = CreateStateGroup();
            var action = CreateSetStateAction(stateGroup, delaySeconds: 10f, stateValue: 1);
            AudioActionEvent actionEvent = CreateActionEvent(action);
            using var cancellation = new CancellationTokenSource();

            try
            {
                actionEvent.Execute((GameObject)null, cancellation.Token);
                cancellation.Cancel();

                yield return null;
                yield return null;

                Assert.AreEqual(0, stateGroup.CurrentValue);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actionEvent);
                UnityEngine.Object.DestroyImmediate(stateGroup);
            }
        }

        [UnityTest]
        public IEnumerator Execute_InfiniteDelay_CanBeCanceledWithoutRunningAction()
        {
            AudioStateGroup stateGroup = CreateStateGroup();
            var action = CreateSetStateAction(
                stateGroup,
                delaySeconds: float.PositiveInfinity,
                stateValue: 1);
            using var cancellation = new CancellationTokenSource();

            try
            {
                action.Execute((GameObject)null, cancellation.Token);
                cancellation.Cancel();

                yield return null;
                yield return null;

                Assert.AreEqual(0, stateGroup.CurrentValue);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stateGroup);
            }
        }

        [Test]
        public void Execute_LegacyOverload_PreservesImmediateBehavior()
        {
            AudioStateGroup stateGroup = CreateStateGroup();
            var action = CreateSetStateAction(stateGroup, delaySeconds: 0f, stateValue: 1);

            try
            {
                action.Execute((GameObject)null);
                Assert.AreEqual(1, stateGroup.CurrentValue);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stateGroup);
            }
        }

        private static AudioStateGroup CreateStateGroup()
        {
            AudioStateGroup stateGroup = ScriptableObject.CreateInstance<AudioStateGroup>();
            SetField(stateGroup, "stateNames", new[] { "Default", "Active" });
            SetField(stateGroup, "defaultValue", 0);
            stateGroup.InitializeStateGroup();
            return stateGroup;
        }

        private static AudioEventAction CreateSetStateAction(
            AudioStateGroup stateGroup,
            float delaySeconds,
            int stateValue)
        {
            var action = new AudioEventAction();
            SetField(action, "actionType", AudioActionType.SetState);
            SetField(action, "delaySeconds", delaySeconds);
            SetField(action, "stateGroup", stateGroup);
            SetField(action, "stateValue", stateValue);
            return action;
        }

        private static AudioActionEvent CreateActionEvent(params AudioEventAction[] actions)
        {
            AudioActionEvent actionEvent = ScriptableObject.CreateInstance<AudioActionEvent>();
            SetField(actionEvent, "actions", actions);
            return actionEvent;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Missing test field '{fieldName}'.");
            field.SetValue(target, value);
        }
    }
}
