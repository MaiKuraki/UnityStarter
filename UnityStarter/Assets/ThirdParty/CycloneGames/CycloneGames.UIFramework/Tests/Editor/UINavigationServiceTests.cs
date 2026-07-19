using System;
using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class UINavigationServiceTests
    {
        private UINavigationService _navigation;
        private List<string> _ids;
        private List<UINavigationEntry> _history;

        [SetUp]
        public void SetUp()
        {
            _navigation = new UINavigationService(8);
            _ids = new List<string>(16);
            _history = new List<UINavigationEntry>(16);
        }

        [Test]
        public void Register_RejectsInvalidEdgesWithoutMutatingGraph()
        {
            Assert.IsFalse(_navigation.Register(null));
            Assert.IsFalse(_navigation.Register(string.Empty));
            Assert.IsFalse(_navigation.Register("Self", "Self"));
            Assert.IsFalse(_navigation.Register("Child", "Missing"));

            Assert.IsTrue(_navigation.Register("Root"));
            object originalContext = new object();
            Assert.IsTrue(_navigation.Register("Child", "Root", originalContext));
            Assert.IsFalse(_navigation.Register("Child", null, new object()));

            Assert.AreEqual(2, _navigation.CopyHistory(_history));
            Assert.AreEqual("Child", _navigation.CurrentWindow);
            Assert.AreEqual("Root", _navigation.GetOpener("Child"));
            Assert.AreSame(originalContext, _navigation.GetContext("Child"));
        }

        [Test]
        public void CopyQueries_ClearAndReuseCallerBuffersInStableOrder()
        {
            _navigation.Register("Root");
            _navigation.Register("Inventory", "Root");
            _navigation.Register("Settings", "Root");
            _navigation.Register("Details", "Inventory");

            _ids.Add("stale");
            Assert.AreEqual(2, _navigation.CopyAncestors("Details", _ids));
            CollectionAssert.AreEqual(new[] { "Root", "Inventory" }, _ids);

            _ids.Add("stale");
            Assert.AreEqual(2, _navigation.CopyChildren("Root", _ids));
            CollectionAssert.AreEqual(new[] { "Inventory", "Settings" }, _ids);

            _history.Add(default);
            Assert.AreEqual(4, _navigation.CopyHistory(_history));
            CollectionAssert.AreEqual(
                new[] { "Root", "Inventory", "Settings", "Details" },
                new[]
                {
                    _history[0].WindowId,
                    _history[1].WindowId,
                    _history[2].WindowId,
                    _history[3].WindowId,
                });
            Assert.Less(_history[0].Sequence, _history[1].Sequence);
            Assert.Less(_history[1].Sequence, _history[2].Sequence);
            Assert.Less(_history[2].Sequence, _history[3].Sequence);
        }

        [Test]
        public void Unregister_Reparent_ReconnectsChildrenAndReportsOnlyRemovedRoot()
        {
            RegisterHierarchy();

            Assert.IsTrue(_navigation.Unregister("Inventory", ChildClosePolicy.Reparent, _ids));

            CollectionAssert.AreEqual(new[] { "Inventory" }, _ids);
            Assert.AreEqual("Root", _navigation.GetOpener("Sword"));
            Assert.AreEqual("Root", _navigation.GetOpener("Shield"));
            _navigation.CopyChildren("Root", _ids);
            CollectionAssert.AreEqual(new[] { "Sword", "Shield" }, _ids);
            Assert.AreEqual("Shield", _navigation.CurrentWindow);
        }

        [Test]
        public void Unregister_Detach_PreservesChildrenAsRoots()
        {
            RegisterHierarchy();

            Assert.IsTrue(_navigation.Unregister("Inventory", ChildClosePolicy.Detach, _ids));

            CollectionAssert.AreEqual(new[] { "Inventory" }, _ids);
            Assert.IsNull(_navigation.GetOpener("Sword"));
            Assert.IsNull(_navigation.GetOpener("Shield"));
            Assert.IsNull(_navigation.ResolveBackTarget("Sword"));
            Assert.AreEqual("Shield", _navigation.CurrentWindow);
        }

        [Test]
        public void Unregister_Cascade_UsesStableParentBeforeChildDepthFirstOrder()
        {
            _navigation.Register("Root");
            _navigation.Register("Inventory", "Root");
            _navigation.Register("Sword", "Inventory");
            _navigation.Register("SwordDetails", "Sword");
            _navigation.Register("Shield", "Inventory");

            Assert.IsTrue(_navigation.Unregister("Inventory", ChildClosePolicy.Cascade, _ids));

            CollectionAssert.AreEqual(
                new[] { "Inventory", "Sword", "SwordDetails", "Shield" },
                _ids);
            Assert.AreEqual(1, _navigation.CopyHistory(_history));
            Assert.AreEqual("Root", _history[0].WindowId);
            Assert.AreEqual("Root", _navigation.CurrentWindow);
            Assert.IsFalse(_navigation.CanNavigateBack);
        }

        [Test]
        public void Unregister_InvalidRequest_ClearsCallerBufferAndPreservesGraph()
        {
            _navigation.Register("Root");
            _ids.Add("stale");

            Assert.IsFalse(_navigation.Unregister("Missing", ChildClosePolicy.Cascade, _ids));

            Assert.IsEmpty(_ids);
            Assert.AreEqual("Root", _navigation.CurrentWindow);
            Assert.AreEqual(1, _navigation.CopyHistory(_history));
        }

        [Test]
        public void Clear_ReleasesEntriesAndResetsSequence()
        {
            _navigation.Register("Root", context: new object());
            _navigation.Register("Child", "Root", new object());

            _navigation.Clear();

            Assert.IsNull(_navigation.CurrentWindow);
            Assert.IsFalse(_navigation.CanNavigateBack);
            Assert.AreEqual(0, _navigation.CopyHistory(_history));
            Assert.IsNull(_navigation.GetContext("Root"));

            Assert.IsTrue(_navigation.Register("AfterClear"));
            _navigation.CopyHistory(_history);
            Assert.AreEqual(1L, _history[0].Sequence);
        }

        [Test]
        public void CallerBufferedQueries_AfterWarmup_DoNotAllocate()
        {
            _navigation.Register("Root");
            _navigation.Register("Inventory", "Root");
            _navigation.Register("Details", "Inventory");

            for (int i = 0; i < 32; i++)
            {
                _navigation.CopyAncestors("Details", _ids);
                _navigation.CopyChildren("Root", _ids);
                _navigation.CopyHistory(_history);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                _navigation.CopyAncestors("Details", _ids);
                _navigation.CopyChildren("Root", _ids);
                _navigation.CopyHistory(_history);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void NullCallerBuffers_AreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => _navigation.CopyAncestors("Any", null));
            Assert.Throws<ArgumentNullException>(() => _navigation.CopyChildren("Any", null));
            Assert.Throws<ArgumentNullException>(() => _navigation.CopyHistory(null));
            Assert.Throws<ArgumentNullException>(
                () => _navigation.Unregister("Any", ChildClosePolicy.Reparent, null));
        }

        private void RegisterHierarchy()
        {
            _navigation.Register("Root");
            _navigation.Register("Inventory", "Root");
            _navigation.Register("Sword", "Inventory");
            _navigation.Register("Shield", "Inventory");
        }
    }
}
