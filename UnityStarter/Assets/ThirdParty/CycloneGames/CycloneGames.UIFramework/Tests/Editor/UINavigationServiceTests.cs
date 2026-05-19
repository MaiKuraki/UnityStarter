using System.Collections.Generic;
using NUnit.Framework;
using CycloneGames.UIFramework.Runtime;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class UINavigationServiceTests
    {
        private UINavigationService _navigation;

        [SetUp]
        public void SetUp()
        {
            _navigation = new UINavigationService();
        }

        [TearDown]
        public void TearDown()
        {
            _navigation.Dispose();
        }

        [Test]
        public void Register_TracksCurrentWindowHistoryAndContext()
        {
            var context = new NavigationContext(17);

            _navigation.Register("MainMenu");
            _navigation.Register("Settings", "MainMenu", context);

            Assert.AreEqual("Settings", _navigation.CurrentWindow);
            Assert.IsTrue(_navigation.CanNavigateBack);
            Assert.AreEqual("MainMenu", _navigation.GetOpener("Settings"));
            Assert.AreSame(context, _navigation.GetContext("Settings"));

            List<UINavigationEntry> history = _navigation.GetHistory();
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual("MainMenu", history[0].WindowName);
            Assert.AreEqual("Settings", history[1].WindowName);
        }

        [Test]
        public void GetAncestors_ReturnsOldestToNewestOpeners()
        {
            _navigation.Register("Root");
            _navigation.Register("Inventory", "Root");
            _navigation.Register("ItemDetails", "Inventory");

            List<string> ancestors = _navigation.GetAncestors("ItemDetails");

            CollectionAssert.AreEqual(new[] { "Root", "Inventory" }, ancestors);
            Assert.AreEqual("Inventory", _navigation.ResolveBackTarget("ItemDetails"));
        }

        [Test]
        public void Unregister_ReparentPolicy_ReconnectsChildrenToClosingNodeOpener()
        {
            _navigation.Register("Root");
            _navigation.Register("Inventory", "Root");
            _navigation.Register("ItemDetails", "Inventory");

            _navigation.Unregister("Inventory", ChildClosePolicy.Reparent);

            Assert.IsNull(_navigation.GetOpener("Inventory"));
            Assert.AreEqual("Root", _navigation.GetOpener("ItemDetails"));
            Assert.AreEqual("Root", _navigation.ResolveBackTarget("ItemDetails"));
            CollectionAssert.AreEqual(new[] { "ItemDetails" }, _navigation.GetChildren("Root"));
        }

        [Test]
        public void Unregister_CascadePolicy_RemovesDescendants()
        {
            _navigation.Register("Root");
            _navigation.Register("Inventory", "Root");
            _navigation.Register("ItemDetails", "Inventory");

            _navigation.Unregister("Inventory", ChildClosePolicy.Cascade);

            Assert.IsNull(_navigation.GetOpener("Inventory"));
            Assert.IsNull(_navigation.GetOpener("ItemDetails"));
            Assert.AreEqual("Root", _navigation.CurrentWindow);
            Assert.IsFalse(_navigation.CanNavigateBack);
        }

        [Test]
        public void Unregister_DetachPolicy_PreservesChildrenAsRoots()
        {
            _navigation.Register("Root");
            _navigation.Register("Inventory", "Root");
            _navigation.Register("ItemDetails", "Inventory");

            _navigation.Unregister("Inventory", ChildClosePolicy.Detach);

            Assert.IsNull(_navigation.GetOpener("ItemDetails"));
            Assert.IsNull(_navigation.ResolveBackTarget("ItemDetails"));
            Assert.AreEqual("ItemDetails", _navigation.CurrentWindow);
        }

        [Test]
        public void DuplicateRegister_KeepsOriginalNodeStable()
        {
            var firstContext = new NavigationContext(1);
            var duplicateContext = new NavigationContext(2);

            _navigation.Register("Popup", null, firstContext);
            _navigation.Register("Popup", "Other", duplicateContext);

            Assert.IsNull(_navigation.GetOpener("Popup"));
            Assert.AreSame(firstContext, _navigation.GetContext("Popup"));
            Assert.AreEqual(1, _navigation.GetHistory().Count);
        }

        private sealed class NavigationContext
        {
            public readonly int Value;

            public NavigationContext(int value)
            {
                Value = value;
            }
        }
    }
}
