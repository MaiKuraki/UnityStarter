using CycloneGames.Scheduling.Core;
using CycloneGames.Scheduling.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Scheduling.Tests.Editor
{
    public sealed class SchedulingDriverTests
    {
        [Test]
        public void Shutdown_TerminatesOutstandingWorkAndIsIdempotent()
        {
            var host = new GameObject("SchedulingDriverTests");
            var driver = host.AddComponent<SchedulingDriver>();
            var item = new TestWorkItem(int.MaxValue);
            driver.Scheduler.TrySchedule(item, out _);

            driver.Shutdown();
            driver.Shutdown();

            Assert.That(item.StepCount, Is.Zero);
            Assert.That(item.TerminationCount, Is.EqualTo(1));
            Assert.That(item.Reason, Is.EqualTo(WorkTerminationReason.Shutdown));
            Assert.Throws<System.InvalidOperationException>(() => _ = driver.Scheduler);
            Object.DestroyImmediate(host);
        }
    }
}
