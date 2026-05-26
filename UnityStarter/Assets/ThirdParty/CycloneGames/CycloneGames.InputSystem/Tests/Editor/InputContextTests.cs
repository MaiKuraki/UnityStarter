using System;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace CycloneGames.InputSystem.Tests.Editor
{
    public sealed class InputContextTests
    {
        [Test]
        public void Constructor_Throws_WhenActionMapNameIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new InputContext(null));
        }

        [Test]
        public void Constructor_UsesActionMapNameAsDefaultName()
        {
            var context = new InputContext("UIActions");

            Assert.AreEqual("UIActions", context.ActionMapName);
            Assert.AreEqual("UIActions", context.Name);
        }

        [Test]
        public void AddBinding_Throws_WhenSourceIsNull()
        {
            var context = new InputContext("UIActions");

            Assert.Throws<ArgumentNullException>(() => context.AddBinding((Observable<Unit>)null, new ActionCommand(() => { })));
            Assert.Throws<ArgumentNullException>(() => context.AddBinding((Observable<Vector2>)null, new MoveCommand(_ => { })));
            Assert.Throws<ArgumentNullException>(() => context.AddBinding((Observable<float>)null, new ScalarCommand(_ => { })));
            Assert.Throws<ArgumentNullException>(() => context.AddBinding((Observable<bool>)null, new BoolCommand(_ => { })));
        }

        [Test]
        public void NullCommand_AllExecuteOverloadsAreNoOp()
        {
            Assert.DoesNotThrow(() => NullCommand.Instance.Execute());
            Assert.DoesNotThrow(() => NullCommand.Instance.Execute(Vector2.one));
            Assert.DoesNotThrow(() => NullCommand.Instance.Execute(1f));
            Assert.DoesNotThrow(() => NullCommand.Instance.Execute(true));
        }
    }
}
