using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Progressing;
using NUnit.Framework;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class ProgressAggregatorTests
    {
        [Test]
        public void GetProgress_Returns_Complete_When_Empty()
        {
            var aggregator = new ProgressAggregator();

            Assert.AreEqual(1f, aggregator.GetProgress());
        }

        [Test]
        public void GetProgress_Uses_Weighted_Average()
        {
            var aggregator = new ProgressAggregator();
            aggregator.Add(new TestOperation(0.25f), 1f);
            aggregator.Add(new TestOperation(0.75f), 3f);

            Assert.AreEqual(0.625f, aggregator.GetProgress(), 0.0001f);
        }

        [Test]
        public void Add_Clamps_Invalid_Weights_To_One()
        {
            var aggregator = new ProgressAggregator();
            aggregator.Add(new TestOperation(0.25f), float.NaN);
            aggregator.Add(new TestOperation(0.75f), -1f);

            Assert.AreEqual(0.5f, aggregator.GetProgress(), 0.0001f);
        }

        [Test]
        public void GetProgress_Clamps_Result_To_One()
        {
            var aggregator = new ProgressAggregator();
            aggregator.Add(new TestOperation(2f));

            Assert.AreEqual(1f, aggregator.GetProgress());
        }
    }
}
