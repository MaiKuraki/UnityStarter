using CycloneGames.Cheat.Core;
using CycloneGames.Cheat.Runtime;
using NUnit.Framework;

#if ENABLE_CHEAT
using System.Threading.Tasks;
#endif

namespace CycloneGames.Cheat.Tests.Editor
{
    public sealed class CheatRuntimeTests
    {
#if !ENABLE_CHEAT
        [Test]
        public void DisabledRuntimeIsSafeNoOp()
        {
            using var runtime = new CheatCommandRuntime();

            runtime.CancelCommand("Command");
            runtime.ClearAll();

            Assert.False(runtime.IsEnabled);
            Assert.False(runtime.IsCommandRunning("Command"));
            Assert.AreEqual(0, runtime.RunningCommandCount);
            Assert.AreEqual(0, runtime.Metrics.PublishedCommandCount);
        }
#else
        [Test]
        public async Task EnabledRuntimePublishesCommandAndUpdatesMetrics()
        {
            using var runtime = new CheatCommandRuntime();

            await runtime.PublishAsync<CheatCommand>(new CheatCommand("Command"));

            Assert.True(runtime.IsEnabled);
            Assert.AreEqual(1, runtime.Metrics.PublishedCommandCount);
            Assert.AreEqual(1, runtime.Metrics.CompletedCommandCount);
        }
#endif
    }
}
