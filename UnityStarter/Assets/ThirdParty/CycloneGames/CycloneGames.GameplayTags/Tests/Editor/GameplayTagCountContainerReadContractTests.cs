using System;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests.Editor
{
    public sealed class GameplayTagCountContainerReadContractTests
    {
        [Test]
        public void ReadInsideNotification_ThrowsButDoesNotLockLaterReads()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagManager.RegisterDynamicTags(new List<string> { "Count.Read.Probe" });
            GameplayTag tag = GameplayTagManager.Request("Count.Read.Probe");

            var container = new GameplayTagCountContainer();
            int rejectedReads = 0;
            container.OnAnyTagCountChange += (changed, newCount) =>
            {
                try
                {
                    _ = container.IsEmpty;
                }
                catch (InvalidOperationException)
                {
                    rejectedReads++;
                }
            };

            container.AddTag(tag);

            Assert.That(rejectedReads, Is.GreaterThanOrEqualTo(1),
                "A read issued from the notification must fail fast.");
            Assert.That(container.IsEmpty, Is.False,
                "A rejected read must not lock the container for later reads.");
            Assert.That(container.GetTagCount(tag), Is.EqualTo(1));
        }
    }
}
