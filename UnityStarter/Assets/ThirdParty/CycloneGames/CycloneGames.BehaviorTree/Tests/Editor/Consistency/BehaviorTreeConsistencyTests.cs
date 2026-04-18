using System;
using System.IO;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Networking;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.DOD;
using CycloneGames.BehaviorTree.Tests.Editor.Framework;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public class BehaviorTreeConsistencyTests
    {
        [Test]
        public void RuntimeBehaviorTree_StopPreventsFurtherTicksUntilPlay()
        {
            var leaf = new FixedStateNode(RuntimeState.Success);
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(leaf);

            try
            {
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(1, leaf.RunCount);

                tree.Stop();

                Assert.IsTrue(tree.IsStopped);
                Assert.AreEqual(RuntimeState.NotEntered, tree.Tick());
                Assert.AreEqual(1, leaf.RunCount);

                tree.Play();

                Assert.IsFalse(tree.IsStopped);
                Assert.AreEqual(RuntimeState.Success, tree.Tick());
                Assert.AreEqual(2, leaf.RunCount);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RuntimeBehaviorTree_StopAbortsRunningStatefulActions()
        {
            var leaf = new RecordingStatefulActionNode(RuntimeState.Running, RuntimeState.Running);
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(leaf);

            try
            {
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(1, leaf.StartCount);

                tree.Stop();

                Assert.IsTrue(tree.IsStopped);
                Assert.AreEqual(1, leaf.HaltCount);
                Assert.AreEqual(RuntimeState.NotEntered, tree.State);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void SelectorLowerPriorityAbortInterruptsRunningChild()
        {
            int interruptKey = Animator.StringToHash("Interrupt");
            var highPriority = new ConditionalRunningNode(interruptKey, true);
            var lowPriority = new RecordingStatefulActionNode(RuntimeState.Running, RuntimeState.Running);
            var selector = BehaviorTreeTestFactory.CreateSelector(highPriority, lowPriority);
            selector.AbortType = RuntimeAbortType.LowerPriority;

            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(selector);

            try
            {
                tree.Blackboard.SetBool(interruptKey, false);
                Assert.AreEqual(RuntimeState.Running, tree.Tick());
                Assert.AreEqual(1, lowPriority.StartCount);
                Assert.AreEqual(1, highPriority.RunCount);

                tree.Blackboard.SetBool(interruptKey, true);
                Assert.AreEqual(RuntimeState.Running, tree.Tick());

                Assert.AreEqual(1, lowPriority.HaltCount);
                Assert.GreaterOrEqual(highPriority.RunCount, 2);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void RuntimeBlackboard_HashAndSerializationRemainDeterministic()
        {
            const int intKey = 101;
            const int floatKey = 202;
            const int boolKey = 303;
            const int vectorKey = 404;

            var a = new RuntimeBlackboard();
            var b = new RuntimeBlackboard();
            var restored = new RuntimeBlackboard();

            try
            {
                a.SetInt(intKey, 7);
                a.SetFloat(floatKey, 1.25f);
                a.SetBool(boolKey, true);
                a.SetVector3(vectorKey, new Vector3(2f, 3f, 4f));

                b.SetVector3(vectorKey, new Vector3(2f, 3f, 4f));
                b.SetBool(boolKey, true);
                b.SetFloat(floatKey, 1.25f);
                b.SetInt(intKey, 7);

                Assert.AreEqual(a.ComputeHash(), b.ComputeHash());

                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);
                a.WriteTo(writer);
                writer.Flush();
                stream.Position = 0;

                using var reader = new BinaryReader(stream);
                restored.ReadFrom(reader);

                Assert.AreEqual(a.ComputeHash(), restored.ComputeHash());
                Assert.AreEqual(7, restored.GetInt(intKey));
                Assert.AreEqual(1.25f, restored.GetFloat(floatKey));
                Assert.IsTrue(restored.GetBool(boolKey));
                Assert.AreEqual(new Vector3(2f, 3f, 4f), restored.GetVector3(vectorKey));
            }
            finally
            {
                a.Dispose();
                b.Dispose();
                restored.Dispose();
            }
        }

        [Test]
        public void BlackboardDelta_RoundTripsMutationsAndRemovals()
        {
            const int intKey = 111;
            const int boolKey = 222;
            var source = new RuntimeBlackboard();
            var target = new RuntimeBlackboard();
            var delta = new BTBlackboardDelta();

            try
            {
                delta.TrackKey(intKey);
                delta.TrackKey(boolKey);

                source.SetInt(intKey, 5);
                source.SetBool(boolKey, true);

                Assert.IsTrue(delta.TryFlush(source, out var patch1));
                BTBlackboardDelta.Apply(target, patch1);

                Assert.AreEqual(5, target.GetInt(intKey));
                Assert.IsTrue(target.GetBool(boolKey));

                source.SetInt(intKey, 9);
                source.Remove(boolKey);

                Assert.IsTrue(delta.TryFlush(source, out var patch2));
                BTBlackboardDelta.Apply(target, patch2);

                Assert.AreEqual(9, target.GetInt(intKey));
                Assert.IsFalse(target.HasKey(boolKey));
            }
            finally
            {
                source.Dispose();
                target.Dispose();
            }
        }

        [Test]
        public void NetworkSnapshot_RoundTripRestoresBlackboardAndHash()
        {
            const int intKey = 333;
            const int vectorKey = 444;

            var sourceTree = BehaviorTreeTestFactory.CreateRuntimeTree(new FixedStateNode(RuntimeState.Running));
            var targetTree = BehaviorTreeTestFactory.CreateRuntimeTree(new FixedStateNode(RuntimeState.Running));

            try
            {
                sourceTree.Blackboard.SetInt(intKey, 42);
                sourceTree.Blackboard.SetVector3(vectorKey, new Vector3(9f, 8f, 7f));

                var snapshot = BTNetworkSync.CaptureSnapshot(sourceTree);
                var bytes = BTNetworkSync.SerializeSnapshot(snapshot);
                var restoredSnapshot = BTNetworkSync.DeserializeSnapshot(bytes);

                BTNetworkSync.ApplyBlackboardSnapshot(targetTree, restoredSnapshot);

                Assert.AreEqual(sourceTree.Blackboard.ComputeHash(), targetTree.Blackboard.ComputeHash());
                Assert.AreEqual(42, targetTree.Blackboard.GetInt(intKey));
                Assert.AreEqual(new Vector3(9f, 8f, 7f), targetTree.Blackboard.GetVector3(vectorKey));
                Assert.IsFalse(BTNetworkSync.CheckDesync(targetTree, snapshot.BlackboardHash));
            }
            finally
            {
                sourceTree.Dispose();
                targetTree.Dispose();
            }
        }

        [Test]
        public void FlatTreeCompiler_RootOnlyTreeCompilesToSingleNode()
        {
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(null);

            try
            {
                using var flatTree = FlatTreeCompiler.Compile(tree, Allocator.Temp);

                Assert.IsTrue(flatTree.IsCreated);
                Assert.AreEqual(1, flatTree.NodeCount);
                Assert.AreEqual(FlatNodeType.Succeeder, flatTree.Nodes[0].Type);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void FlatTreeCompiler_UnsupportedManagedLeafFailsFast()
        {
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(new FixedStateNode(RuntimeState.Success));

            try
            {
                var exception = Assert.Throws<NotSupportedException>(() => FlatTreeCompiler.Compile(tree, Allocator.Temp));
                StringAssert.Contains("managed leaf node", exception.Message);
            }
            finally
            {
                tree.Dispose();
            }
        }

        [Test]
        public void FlatTreeCompiler_UnsupportedDecoratorFailsFast()
        {
            var decorator = new UnsupportedDecoratorNode
            {
                Child = null
            };
            var tree = BehaviorTreeTestFactory.CreateRuntimeTree(decorator);

            try
            {
                var exception = Assert.Throws<NotSupportedException>(() => FlatTreeCompiler.Compile(tree, Allocator.Temp));
                StringAssert.Contains("decorator node", exception.Message);
            }
            finally
            {
                tree.Dispose();
            }
        }
    }
}
