using System;
using System.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime.Batch;
using NUnit.Framework;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class GroupOperationTests
    {
        [Test]
        public void Items_Returns_Stable_Snapshot_Until_Add()
        {
            var group = new GroupOperation();
            var first = new TestOperation(1f);
            var second = new TestOperation(1f);

            group.Add(first);
            var items = group.Items;
            Assert.AreEqual(1, items.Count);
            Assert.AreSame(first, items[0]);

            group.Add(second);
            Assert.AreEqual(2, group.Items.Count);
            Assert.AreSame(second, group.Items[1]);
        }

        [Test]
        public async Task StartAsync_Completes_Items_In_Order_And_Updates_Progress()
        {
            var group = new GroupOperation();
            group.Add(new TestOperation(1f), 1f);
            group.Add(new TestOperation(1f), 3f);

            await group.StartAsync();

            Assert.IsTrue(group.IsDone);
            Assert.AreEqual(1f, group.Progress);
            Assert.IsNull(group.Error);
        }

        [Test]
        public async Task StartAsync_Captures_First_Item_Error()
        {
            var group = new GroupOperation();
            group.Add(new TestOperation(1f, "missing asset"));
            group.Add(new TestOperation(1f, "second error"));

            await group.StartAsync();
            var exception = Assert.ThrowsAsync<Exception>(async () => await group.Task);

            Assert.IsTrue(group.IsDone);
            Assert.AreEqual("missing asset", group.Error);
            Assert.AreEqual("missing asset", exception.Message);
        }

        [Test]
        public async Task Add_Throws_After_StartAsync_Completes()
        {
            var group = new GroupOperation();
            group.Add(new TestOperation(1f));

            await group.StartAsync();

            Assert.Throws<InvalidOperationException>(() => group.Add(new TestOperation(1f)));
        }
    }
}
