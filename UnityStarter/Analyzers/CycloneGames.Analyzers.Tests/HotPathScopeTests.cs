using System.Reflection;
using NUnit.Framework;

namespace CycloneGames.Analyzers.Tests
{
    /// <summary>
    /// Regression guard for the pose backend's hot-path gate. The names are scoped by namespace so that
    /// enabling CG0001/CG0002/CG0003 for Commit-style methods cannot flood unrelated modules.
    /// </summary>
    [TestFixture]
    public sealed class HotPathScopeTests
    {
        private static bool IsHot(string methodName, string namespaceName)
        {
            MethodInfo predicate = typeof(HotPathAllocationAnalyzer).GetMethod(
                "IsHotPathMethod", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.That(predicate, Is.Not.Null, "IsHotPathMethod must remain the single hot-path gate.");
            return (bool)predicate.Invoke(null, new object[] { methodName, namespaceName })!;
        }

        [TestCase("Commit")]
        [TestCase("BeginPlayback")]
        [TestCase("UpdatePlayback")]
        [TestCase("EndPlayback")]
        [TestCase("FadePlayback")]
        [TestCase("SeekPlayback")]
        [TestCase("ApplyLayer")]
        [TestCase("QueueClipEvent")]
        [TestCase("DrainClipEvents")]
        [TestCase("DrainTransitionEvents")]
        [TestCase("QueueClockSpan")]
        [TestCase("HandleAnimatorMove")]
        [TestCase("TickFrame")]
        [TestCase("CommitNow")]
        [TestCase("AdvanceTransition")]
        [TestCase("AdvanceLayerFade")]
        [TestCase("EvaluateClock")]
        [TestCase("ComputePoseHash")]
        [TestCase("ResetToTick")]
        public void PoseBackendCommitPathIsGuarded(string methodName)
        {
            Assert.That(IsHot(methodName, "CycloneGames.AnimationRuntime"), Is.True);
            Assert.That(IsHot(methodName, "CycloneGames.AnimationRuntime.Core"), Is.True);
        }

        [TestCase("Commit")]
        [TestCase("BeginPlayback")]
        [TestCase("ApplyLayer")]
        public void UnrelatedModulesAreNotNewlyGuarded(string methodName)
        {
            Assert.That(IsHot(methodName, "CycloneGames.DataTable.CodeGen"), Is.False);
            Assert.That(IsHot(methodName, "SomeOther.Module"), Is.False);
        }

        [TestCase("Update")]
        [TestCase("LateUpdate")]
        [TestCase("FixedUpdate")]
        [TestCase("OnTick")]
        public void GlobalHotPathNamesRemainUnscoped(string methodName)
        {
            Assert.That(IsHot(methodName, "Any.Namespace"), Is.True);
        }

        [Test]
        public void UnknownMethodIsNotAHotPath()
        {
            Assert.That(IsHot("ToString", "CycloneGames.AnimationRuntime"), Is.False);
        }
    }
}
