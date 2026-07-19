using System;
using System.IO;

using CycloneGames.IO.Unity;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.IO.Tests.Unity
{
    public sealed class UnityFileUriTests
    {
        [Test]
        public void Create_PersistentDataPath_ReturnsContainedFileUri()
        {
            string uri = UnityFileUri.Create("settings/input.yaml", UnityFileLocation.PersistentData);

            var parsed = new Uri(uri);
            Assert.That(parsed.IsFile, Is.True);
            Assert.That(
                Path.GetFullPath(parsed.LocalPath).StartsWith(
                    Path.GetFullPath(Application.persistentDataPath),
                    StringComparison.OrdinalIgnoreCase),
                Is.True);
        }

        [Test]
        public void TryCreate_TraversalPath_ReturnsTypedFailure()
        {
            bool success = UnityFileUri.TryCreate(
                "../outside.yaml",
                UnityFileLocation.PersistentData,
                out string uri,
                out UnityFileUriError error);

            Assert.That(success, Is.False);
            Assert.That(uri, Is.Null);
            Assert.That(error, Is.EqualTo(UnityFileUriError.InvalidPath));
        }

        [Test]
        public void TryCreate_UnsupportedScheme_ReturnsTypedFailure()
        {
            bool success = UnityFileUri.TryCreate(
                "ftp://example.com/content.bin",
                UnityFileLocation.AbsolutePathOrUri,
                out string uri,
                out UnityFileUriError error);

            Assert.That(success, Is.False);
            Assert.That(uri, Is.Null);
            Assert.That(error, Is.EqualTo(UnityFileUriError.UnsupportedScheme));
        }
    }
}
