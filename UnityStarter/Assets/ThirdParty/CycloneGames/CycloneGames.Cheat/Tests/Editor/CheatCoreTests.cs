using System;
using CycloneGames.Cheat.Core;
using NUnit.Framework;

namespace CycloneGames.Cheat.Tests.Editor
{
    public sealed class CheatCoreTests
    {
        [Test]
        public void CommandConstructorsRejectNullValues()
        {
            Assert.Throws<ArgumentNullException>(() => new CheatCommand(null));
            Assert.Throws<ArgumentNullException>(() => new CheatCommand<TestPayload>(null, new TestPayload(1)));
            Assert.Throws<ArgumentNullException>(() => new CheatCommandClass<string>(null, "value"));
            Assert.Throws<ArgumentNullException>(() => new CheatCommandClass<string>("Command", null));
        }

        [Test]
        public void MetricsKeepPassedValues()
        {
            var metrics = new CheatRuntimeMetrics(
                runningCommandCount: 1,
                publishedCommandCount: 2,
                completedCommandCount: 3,
                droppedDuplicateCount: 4,
                cancelRequestedCount: 5,
                faultedCommandCount: 6);

            Assert.AreEqual(1, metrics.RunningCommandCount);
            Assert.AreEqual(2, metrics.PublishedCommandCount);
            Assert.AreEqual(3, metrics.CompletedCommandCount);
            Assert.AreEqual(4, metrics.DroppedDuplicateCount);
            Assert.AreEqual(5, metrics.CancelRequestedCount);
            Assert.AreEqual(6, metrics.FaultedCommandCount);
        }

        private readonly struct TestPayload
        {
            public readonly int Value;

            public TestPayload(int value)
            {
                Value = value;
            }
        }
    }
}
