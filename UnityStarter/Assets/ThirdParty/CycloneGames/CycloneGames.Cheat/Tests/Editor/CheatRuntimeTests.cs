using CycloneGames.Cheat.Core;
using CycloneGames.Cheat.Runtime;
using NUnit.Framework;

#if ENABLE_CHEAT
using System.Threading.Tasks;
using VitalRouter;
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
        public async Task EnabledRuntimePublishesThroughRouter()
        {
            var router = new Router();
            using var runtime = new CheatCommandRuntime();
            int received = 0;

            using var subscription = router.Subscribe<CheatCommand>((command, _) =>
            {
                if (command.CommandId == "Command")
                {
                    received++;
                }
            });

            await runtime.PublishAsync("Command", router);

            Assert.True(runtime.IsEnabled);
            Assert.AreEqual(1, received);
            Assert.AreEqual(1, runtime.Metrics.PublishedCommandCount);
            Assert.AreEqual(1, runtime.Metrics.CompletedCommandCount);
        }
#endif
    }
}
