using CycloneGames.Audio.Runtime;
using NUnit.Framework;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioRandomSelectionTests
    {
        [Test]
        public void ClampAvoidRepeatHistory_Limits_To_Available_Alternatives()
        {
            Assert.AreEqual(0, AudioRandomSelectionUtility.ClampAvoidRepeatHistory(8, 1));
            Assert.AreEqual(1, AudioRandomSelectionUtility.ClampAvoidRepeatHistory(0, 4));
            Assert.AreEqual(3, AudioRandomSelectionUtility.ClampAvoidRepeatHistory(8, 4));
        }

        [Test]
        public void IsInRecentHistory_Reads_Ring_Buffer_In_Reverse_Write_Order()
        {
            int[] history = { 1, 2, 3, 4 };

            Assert.IsTrue(AudioRandomSelectionUtility.IsInRecentHistory(1, 3, history, 1, 4));
            Assert.IsTrue(AudioRandomSelectionUtility.IsInRecentHistory(4, 3, history, 1, 4));
            Assert.IsTrue(AudioRandomSelectionUtility.IsInRecentHistory(3, 3, history, 1, 4));
            Assert.IsFalse(AudioRandomSelectionUtility.IsInRecentHistory(2, 3, history, 1, 4));
        }

        [Test]
        public void CountEligible_Excludes_Recent_History()
        {
            int[] history = { 1, 3 };

            int eligibleCount = AudioRandomSelectionUtility.CountEligible(4, 2, history, 2, 2);

            Assert.AreEqual(2, eligibleCount);
        }

        [Test]
        public void SelectUniform_Maps_Ordinal_To_Non_Recent_Node()
        {
            int[] history = { 1, 3 };

            int selected = AudioRandomSelectionUtility.SelectUniform(4, 2, history, 2, 2, 1, 0);

            Assert.AreEqual(2, selected);
        }

        [Test]
        public void SelectUniform_Falls_Back_When_All_Nodes_Are_Excluded()
        {
            int[] history = { 0, 1 };

            int selected = AudioRandomSelectionUtility.SelectUniform(2, 2, history, 0, 2, 0, 1);

            Assert.AreEqual(1, selected);
        }

        [Test]
        public void CalculateEligibleWeight_Ignores_Negative_Weights_And_Recent_History()
        {
            float[] weights = { 2f, -5f, 3f, 4f };
            int[] history = { 2 };

            float totalWeight = AudioRandomSelectionUtility.CalculateEligibleWeight(weights, 4, 1, history, 1, 1);

            Assert.AreEqual(6f, totalWeight);
        }

        [Test]
        public void SelectWeighted_Uses_Cumulative_Weight_Ranges()
        {
            float[] weights = { 1f, 2f, 3f };

            Assert.AreEqual(0, AudioRandomSelectionUtility.SelectWeighted(weights, 3, 0, null, 0, 0, 0.99f));
            Assert.AreEqual(1, AudioRandomSelectionUtility.SelectWeighted(weights, 3, 0, null, 0, 0, 1f));
            Assert.AreEqual(1, AudioRandomSelectionUtility.SelectWeighted(weights, 3, 0, null, 0, 0, 2.99f));
            Assert.AreEqual(2, AudioRandomSelectionUtility.SelectWeighted(weights, 3, 0, null, 0, 0, 3f));
            Assert.AreEqual(2, AudioRandomSelectionUtility.SelectWeighted(weights, 3, 0, null, 0, 0, 6f));
        }

        [Test]
        public void SelectWeighted_Skips_Recent_History()
        {
            float[] weights = { 5f, 10f, 1f };
            int[] history = { 1 };

            Assert.AreEqual(0, AudioRandomSelectionUtility.SelectWeighted(weights, 3, 1, history, 1, 1, 4.99f));
            Assert.AreEqual(2, AudioRandomSelectionUtility.SelectWeighted(weights, 3, 1, history, 1, 1, 5f));
        }
    }
}
