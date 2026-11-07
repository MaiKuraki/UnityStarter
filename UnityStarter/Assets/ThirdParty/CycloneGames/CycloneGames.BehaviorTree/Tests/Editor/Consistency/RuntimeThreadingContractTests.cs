using System;
using System.Threading;
using CycloneGames.BehaviorTree.Core;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    /// <summary>
    /// Locks the thread contract: the tree is owner-thread only, while reevaluation signalling is
    /// accepted from any producer thread and coalesced into one epoch.
    /// </summary>
    public sealed class RuntimeThreadingContractTests
    {
        [Test]
        public void TickFromForeignThread_IsRejected()
        {
            using RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .Action(_ => RuntimeState.Success)
                .Build();

            tree.Tick();

            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    tree.Tick();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            thread.Start();
            thread.Join();

            Assert.That(captured, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void ReevaluationSignalFromForeignThread_IsAcceptedAndAppliedOnOwnerThread()
        {
            int evaluations = 0;
            using RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
                .Action(_ =>
                {
                    evaluations++;
                    return RuntimeState.Running;
                })
                .Build();

            tree.Tick();
            int before = tree.Evaluation.Epoch;

            var thread = new Thread(() => tree.RequestReevaluation());
            thread.Start();
            thread.Join();

            Assert.Greater(tree.Evaluation.Epoch, before, "the epoch must advance from a producer thread");
            Assert.DoesNotThrow(() => tree.Tick());
            Assert.GreaterOrEqual(evaluations, 2, "the owner thread must observe the signal on its next tick");
        }
    }
}
