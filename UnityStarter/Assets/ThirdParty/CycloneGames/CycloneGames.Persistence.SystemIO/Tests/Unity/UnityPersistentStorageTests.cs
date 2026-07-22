using System;
using CycloneGames.Persistence.Unity;
using NUnit.Framework;

namespace CycloneGames.Persistence.Tests.Unity
{
    [TestFixture]
    public sealed class UnityPersistentStorageTests
    {
        [TestCase("../outside.save")]
        [TestCase("Saves/../../outside.save")]
        [TestCase("Saves//slot.save")]
        [TestCase("CON.save")]
        public void Create_RejectsNonPortableRelativePath(string relativePath)
        {
            Assert.Throws<ArgumentException>(() =>
                UnityPersistentStorage.Create(relativePath));
        }

        [Test]
        public void Create_ReturnsStorageContract()
        {
            IPersistenceStorage storage =
                UnityPersistentStorage.Create("Saves/slot-001.save");

            Assert.That(storage, Is.Not.Null);
            Assert.That(storage.Location, Does.EndWith("slot-001.save"));
        }
    }
}
