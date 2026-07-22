using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using CycloneGames.BehaviorTree.Runtime;
using CycloneGames.BehaviorTree.Runtime.Components;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CycloneGames.BehaviorTree.Tests.PlayMode
{
    public sealed class BehaviorTreeRunnerLifecycleTests
    {
        [UnityTest]
        public IEnumerator ManagedRunner_PauseDisableStopAndPlayOwnManagerRegistration()
        {
            yield return VerifyManagedLifecycle(TickMode.Managed);
        }

        [UnityTest]
        public IEnumerator PriorityRunner_PauseDisableStopAndPlayOwnManagerRegistration()
        {
            yield return VerifyManagedLifecycle(TickMode.PriorityManaged);
        }

        [UnityTest]
        public IEnumerator ManagedRunner_ReRegistersAfterManagerRecreation()
        {
            yield return VerifyManagerRecreation(TickMode.Managed);
        }

        [UnityTest]
        public IEnumerator PriorityRunner_ReRegistersAfterManagerRecreation()
        {
            yield return VerifyManagerRecreation(TickMode.PriorityManaged);
        }

        [UnityTest]
        public IEnumerator DuplicateTickManagerDestroysOnlyDuplicateComponent()
        {
            BTTickManagerComponent primary = BTTickManagerComponent.Instance;
            var duplicateObject = new GameObject("Duplicate-BTTickManager");
            duplicateObject.AddComponent<BTTickManagerComponent>();

            yield return null;

            Assert.That(duplicateObject != null, Is.True);
            Assert.That(duplicateObject.GetComponent<BTTickManagerComponent>(), Is.Null);
            Assert.That(BTTickManagerComponent.Instance, Is.SameAs(primary));
            Object.Destroy(duplicateObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DuplicatePriorityTickManagerDestroysOnlyDuplicateComponent()
        {
            BTPriorityTickManagerComponent primary = BTPriorityTickManagerComponent.Instance;
            var duplicateObject = new GameObject("Duplicate-BTPriorityTickManager");
            duplicateObject.AddComponent<BTPriorityTickManagerComponent>();

            yield return null;

            Assert.That(duplicateObject != null, Is.True);
            Assert.That(duplicateObject.GetComponent<BTPriorityTickManagerComponent>(), Is.Null);
            Assert.That(BTPriorityTickManagerComponent.Instance, Is.SameAs(primary));
            Object.Destroy(duplicateObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ManagedRunner_UsesDeterministicSceneManagerBeforeItsAwake()
        {
            yield return VerifyPreAwakeManagerSelection(TickMode.Managed);
        }

        [UnityTest]
        public IEnumerator PriorityRunner_UsesDeterministicSceneManagerBeforeItsAwake()
        {
            yield return VerifyPreAwakeManagerSelection(TickMode.PriorityManaged);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator AuthoringAssetMutators_RejectDuringPlayMode()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var child = ScriptableObject.CreateInstance<WaitNode>();
            var composite = ScriptableObject.CreateInstance<SequencerNode>();
            var validationNode = ScriptableObject.CreateInstance<PlayModeValidationCountingNode>();
            try
            {
                tree.Root = root;
                tree.Nodes.Add(root);
                root.Tree = tree;
                child.Tree = tree;

                Assert.Throws<InvalidOperationException>(() => tree.CreateNode(typeof(WaitNode)));
                Assert.Throws<InvalidOperationException>(() => tree.AddNode(child));
                Assert.Throws<InvalidOperationException>(() => tree.DeleteNode(root));
                Assert.Throws<InvalidOperationException>(() => tree.AddChild(root, child));
                Assert.Throws<InvalidOperationException>(() => tree.RemoveChild(root, child));
                Assert.Throws<InvalidOperationException>(() => tree.NormalizeCompositeChildren(composite));
                Assert.Throws<InvalidOperationException>(tree.NotifyTreeChanged);

                tree.Nodes.Add(null);
                tree.OnValidate();
                validationNode.OnValidate();
                Assert.That(tree.Nodes, Has.Count.EqualTo(2));
                Assert.That(tree.Nodes[1], Is.Null);
                Assert.That(root.Child, Is.Null);
                Assert.That(validationNode.ValidationCount, Is.Zero);
            }
            finally
            {
                Object.Destroy(validationNode);
                Object.Destroy(composite);
                Object.Destroy(child);
                Object.Destroy(root);
                Object.Destroy(tree);
            }

            yield return null;
        }
#endif

        [UnityTest]
        public IEnumerator Runner_PlayBlackboardObserverFailureLeavesStoppedAndUnregistered()
        {
            const int observedKey = 8123;
            Runtime.BehaviorTree asset = CreateLongRunningTree();
            var gameObject = new GameObject("BehaviorTreeRunner-PlayFailure");
            gameObject.SetActive(false);
            BTRunnerComponent runner = gameObject.AddComponent<BTRunnerComponent>();
            SetField(runner, "behaviorTree", asset);
            SetField(runner, "_tickMode", TickMode.Managed);
            SetField(runner, "_startOnAwake", false);
            gameObject.SetActive(true);
            yield return null;

            runner.Play();
            RuntimeBehaviorTree runtimeTree = runner.RuntimeTree;
            runtimeTree.Blackboard.SetInt(observedKey, 1);
            BlackboardObserverCallback throwingObserver = (_, _) =>
                throw new InvalidOperationException("Expected observer failure.");
            runtimeTree.Blackboard.AddObserver(observedKey, throwingObserver);
            runner.Stop();

            Assert.Throws<AggregateException>(() => runner.Play());
            Assert.That(runner.RuntimeTree, Is.SameAs(runtimeTree));
            Assert.That(runtimeTree.IsStopped, Is.True);
            Assert.That(runner.IsStopped, Is.True);
            Assert.That(runner.IsPaused, Is.True);
            Assert.That(BTTickManagerComponent.Instance.TreeCount, Is.Zero);

            runtimeTree.Blackboard.RemoveObserver(observedKey, throwingObserver);
            Object.Destroy(gameObject);
            yield return null;
            DestroyTree(asset);
        }

        [UnityTest]
        public IEnumerator Runner_CompileFailureKeepsRuntimeNullAndStopped()
        {
            Runtime.BehaviorTree invalidAsset = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var gameObject = new GameObject("BehaviorTreeRunner-CompileFailure");
            gameObject.SetActive(false);
            BTRunnerComponent runner = gameObject.AddComponent<BTRunnerComponent>();
            SetField(runner, "behaviorTree", invalidAsset);
            SetField(runner, "_startOnAwake", false);
            gameObject.SetActive(true);
            yield return null;

            LogAssert.Expect(LogType.Error, new Regex("^\\[BehaviorTree\\]"));
            LogAssert.Expect(
                LogType.Error,
                new Regex("^\\[BTRunnerComponent\\] Behavior tree compilation returned null"));
            Assert.DoesNotThrow(runner.Play);

            Assert.That(runner.RuntimeTree, Is.Null);
            Assert.That(runner.IsStopped, Is.True);
            Assert.That(runner.IsPaused, Is.True);

            Object.Destroy(gameObject);
            Object.Destroy(invalidAsset);
            yield return null;
        }

        private static IEnumerator VerifyManagedLifecycle(TickMode tickMode)
        {
            Runtime.BehaviorTree asset = CreateLongRunningTree();
            var gameObject = new GameObject($"BehaviorTreeRunnerLifecycle-{tickMode}");
            gameObject.SetActive(false);
            BTRunnerComponent runner = gameObject.AddComponent<BTRunnerComponent>();
            SetField(runner, "behaviorTree", asset);
            SetField(runner, "_tickMode", tickMode);
            SetField(runner, "_startOnAwake", false);

            int stoppedEvents = 0;
            runner.OnTreeStopped += () => stoppedEvents++;
            gameObject.SetActive(true);
            yield return null;

            Assert.That(runner.RuntimeTree, Is.Null);
            Assert.That(GetManagerCount(tickMode), Is.Zero);

            var context = new RuntimeBTContext();
            runner.SetContext(context);
            runner.Play();
            Assert.That(runner.RuntimeTree.Context, Is.SameAs(context));
            Assert.That(runner.RuntimeTree.Blackboard.Context, Is.SameAs(context));
            Assert.That(GetManagerCount(tickMode), Is.EqualTo(1));

            yield return null;
            var rejectedContext = new RuntimeBTContext();
            Assert.Throws<InvalidOperationException>(() => runner.SetContext(rejectedContext));
            Assert.That(runner.RuntimeTree.Context, Is.SameAs(context));

            runner.Pause();
            Assert.That(GetManagerCount(tickMode), Is.Zero);

            runner.Resume();
            Assert.That(GetManagerCount(tickMode), Is.EqualTo(1));

            runner.enabled = false;
            Assert.That(GetManagerCount(tickMode), Is.Zero);

            runner.enabled = true;
            Assert.That(GetManagerCount(tickMode), Is.EqualTo(1));

            runner.Stop();
            Assert.That(GetManagerCount(tickMode), Is.Zero);
            Assert.That(stoppedEvents, Is.EqualTo(1));

            runner.Stop();
            Assert.That(stoppedEvents, Is.EqualTo(1));

            runner.Play();
            Assert.That(runner.IsStopped, Is.False);
            Assert.That(GetManagerCount(tickMode), Is.EqualTo(1));

            Object.Destroy(gameObject);
            yield return null;
            Assert.That(GetManagerCount(tickMode), Is.Zero);

            DestroyTree(asset);
        }

        private static IEnumerator VerifyManagerRecreation(TickMode tickMode)
        {
            Runtime.BehaviorTree asset = CreateLongRunningTree();
            var gameObject = new GameObject($"BehaviorTreeRunnerManagerRecreation-{tickMode}");
            gameObject.SetActive(false);
            BTRunnerComponent runner = gameObject.AddComponent<BTRunnerComponent>();
            SetField(runner, "behaviorTree", asset);
            SetField(runner, "_tickMode", tickMode);
            SetField(runner, "_startOnAwake", false);
            gameObject.SetActive(true);
            yield return null;

            runner.Play();
            Component originalManager = tickMode == TickMode.Managed
                ? (Component)BTTickManagerComponent.Instance
                : BTPriorityTickManagerComponent.Instance;
            GameObject originalManagerObject = originalManager.gameObject;
            Assert.That(GetManagerCount(tickMode), Is.EqualTo(1));

            Object.Destroy(originalManager);
            yield return null;
            yield return null;

            if (tickMode == TickMode.PriorityManaged)
            {
                BTDistanceLODProvider retainedProvider = originalManagerObject.GetComponent<BTDistanceLODProvider>();
                Assert.That(retainedProvider, Is.Not.Null);
                Assert.That(retainedProvider.Count, Is.Zero);
            }

            Component replacementManager = tickMode == TickMode.Managed
                ? (Component)BTTickManagerComponent.Instance
                : BTPriorityTickManagerComponent.Instance;
            Assert.That(ReferenceEquals(originalManager, replacementManager), Is.False);
            Assert.That(GetManagerCount(tickMode), Is.EqualTo(1));

            Object.Destroy(gameObject);
            yield return null;
            Assert.That(GetManagerCount(tickMode), Is.Zero);
            Object.Destroy(originalManagerObject);
            Object.Destroy(replacementManager.gameObject);
            yield return null;
            DestroyTree(asset);
        }

        private static IEnumerator VerifyPreAwakeManagerSelection(TickMode tickMode)
        {
            DestroyManagerObjects(tickMode);
            yield return null;

            var firstManagerObject = new GameObject($"ConfiguredManager-A-{tickMode}");
            firstManagerObject.SetActive(false);
            Component firstManager = tickMode == TickMode.Managed
                ? (Component)firstManagerObject.AddComponent<BTTickManagerComponent>()
                : firstManagerObject.AddComponent<BTPriorityTickManagerComponent>();

            var secondManagerObject = new GameObject($"ConfiguredManager-B-{tickMode}");
            secondManagerObject.SetActive(false);
            Component secondManager = tickMode == TickMode.Managed
                ? (Component)secondManagerObject.AddComponent<BTTickManagerComponent>()
                : secondManagerObject.AddComponent<BTPriorityTickManagerComponent>();

            Component expectedManager = firstManager.GetInstanceID() < secondManager.GetInstanceID()
                ? firstManager
                : secondManager;
            Component duplicateManager = ReferenceEquals(expectedManager, firstManager)
                ? secondManager
                : firstManager;
            GameObject duplicateManagerObject = ReferenceEquals(expectedManager, firstManager)
                ? secondManagerObject
                : firstManagerObject;

            Runtime.BehaviorTree asset = CreateLongRunningTree();
            var runnerObject = new GameObject($"BehaviorTreeRunner-PreAwakeManager-{tickMode}");
            runnerObject.SetActive(false);
            BTRunnerComponent runner = runnerObject.AddComponent<BTRunnerComponent>();
            SetField(runner, "behaviorTree", asset);
            SetField(runner, "_tickMode", tickMode);
            SetField(runner, "_startOnAwake", true);

            runnerObject.SetActive(true);
            Component resolvedManager = tickMode == TickMode.Managed
                ? (Component)BTTickManagerComponent.Instance
                : BTPriorityTickManagerComponent.Instance;

            Assert.That(resolvedManager, Is.SameAs(expectedManager));
            Assert.That(resolvedManager.gameObject.name, Does.StartWith("ConfiguredManager-"));
            Assert.That(GetManagerCount(tickMode), Is.EqualTo(1));

            expectedManager.gameObject.SetActive(true);
            duplicateManager.gameObject.SetActive(true);
            yield return null;

            Component remainingDuplicate = tickMode == TickMode.Managed
                ? (Component)duplicateManagerObject.GetComponent<BTTickManagerComponent>()
                : duplicateManagerObject.GetComponent<BTPriorityTickManagerComponent>();
            Assert.That(remainingDuplicate, Is.Null);
            Assert.That(GetManagerCount(tickMode), Is.EqualTo(1));

            Object.Destroy(runnerObject);
            Object.Destroy(firstManagerObject);
            Object.Destroy(secondManagerObject);
            yield return null;
            DestroyTree(asset);
        }

        private static void DestroyManagerObjects(TickMode tickMode)
        {
            if (tickMode == TickMode.Managed)
            {
                BTTickManagerComponent[] managers = Object.FindObjectsOfType<BTTickManagerComponent>(true);
                for (int i = 0; i < managers.Length; i++)
                {
                    if (managers[i] != null)
                    {
                        Object.Destroy(managers[i].gameObject);
                    }
                }

                return;
            }

            BTPriorityTickManagerComponent[] priorityManagers =
                Object.FindObjectsOfType<BTPriorityTickManagerComponent>(true);
            for (int i = 0; i < priorityManagers.Length; i++)
            {
                if (priorityManagers[i] != null)
                {
                    Object.Destroy(priorityManagers[i].gameObject);
                }
            }
        }

        private static int GetManagerCount(TickMode tickMode)
        {
            return tickMode == TickMode.Managed
                ? BTTickManagerComponent.Instance.TreeCount
                : BTPriorityTickManagerComponent.Instance.TotalTreeCount;
        }

        private static Runtime.BehaviorTree CreateLongRunningTree()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var wait = ScriptableObject.CreateInstance<WaitNode>();
            wait.Duration = 1000f;
            root.Child = wait;
            tree.Root = root;
            return tree;
        }

        private static void DestroyTree(Runtime.BehaviorTree tree)
        {
            if (tree == null) return;
            BTRootNode root = tree.Root as BTRootNode;
            if (root != null && root.Child != null) Object.Destroy(root.Child);
            if (root != null) Object.Destroy(root);
            Object.Destroy(tree);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = typeof(BTRunnerComponent).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {fieldName}.");
            field.SetValue(target, value);
        }

#if UNITY_EDITOR
        private sealed class PlayModeValidationCountingNode : BTNode
        {
            public int ValidationCount { get; private set; }

            protected override void CheckIntegrity()
            {
                ValidationCount++;
            }
        }
#endif
    }
}
