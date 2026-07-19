using System;
using System.IO;

using NUnit.Framework;

namespace CycloneGames.IO.Tests.Core
{
    public sealed class FilePathSandboxTests
    {
        private string _rootPath;

        [SetUp]
        public void SetUp()
        {
            _rootPath = Path.Combine(
                Path.GetTempPath(),
                "CycloneGames.IO.Core.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, true);
            }
        }

        [Test]
        public void Resolve_PortableRelativePath_ReturnsContainedAbsolutePath()
        {
            var sandbox = new FilePathSandbox(_rootPath);

            string result = sandbox.Resolve("content/catalog.bin");

            Assert.That(sandbox.ContainsAbsolutePath(result), Is.True);
            Assert.That(result, Is.EqualTo(Path.Combine(_rootPath, "content", "catalog.bin")));
        }

        [TestCase("../outside.bin")]
        [TestCase("content/../../outside.bin")]
        [TestCase("content//catalog.bin")]
        [TestCase("CON.txt")]
        [TestCase("name. ")]
        public void Resolve_UnsafeRelativePath_Throws(string relativePath)
        {
            var sandbox = new FilePathSandbox(_rootPath);

            Assert.Throws<ArgumentException>(() => sandbox.Resolve(relativePath));
        }

        [Test]
        public void ContainsAbsolutePath_SiblingPrefix_ReturnsFalse()
        {
            var sandbox = new FilePathSandbox(_rootPath);
            string siblingPath = _rootPath + "-sibling";

            Assert.That(sandbox.ContainsAbsolutePath(siblingPath), Is.False);
        }
    }
}
