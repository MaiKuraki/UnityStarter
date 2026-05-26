using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    public sealed class InputHashUtilityTests
    {
        [Test]
        public void GetDeterministicHashCode_ReturnsStableKnownValue()
        {
            Assert.AreEqual(1073456347, InputHashUtility.GetDeterministicHashCode("Gameplay/Move"));
        }

        [Test]
        public void GetActionId_MatchesLogicalConcatenatedPath()
        {
            int direct = InputHashUtility.GetActionId("Gameplay", "Move");
            int concatenated = InputHashUtility.GetDeterministicHashCode("Gameplay/Move");

            Assert.AreEqual(concatenated, direct);
        }

        [Test]
        public void GetActionId_WithContext_SeparatesSameActionAcrossContexts()
        {
            int gameplay = InputHashUtility.GetActionId("Gameplay", "PlayerActions", "Confirm");
            int ui = InputHashUtility.GetActionId("UI", "PlayerActions", "Confirm");

            Assert.AreNotEqual(gameplay, ui);
        }

        [Test]
        public void GetActionId_ReturnsZeroForInvalidParts()
        {
            Assert.AreEqual(0, InputHashUtility.GetActionId(null, "Move"));
            Assert.AreEqual(0, InputHashUtility.GetActionId("Gameplay", string.Empty));
            Assert.AreEqual(0, InputHashUtility.GetActionId("Gameplay", "PlayerActions", null));
        }
    }
}
