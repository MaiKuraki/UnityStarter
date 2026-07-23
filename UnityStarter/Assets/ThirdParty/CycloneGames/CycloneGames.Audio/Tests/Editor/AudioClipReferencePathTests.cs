using System.IO;
using System.Threading;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioClipReferencePathTests
    {
        [Test]
        public void ResolveLocation_StreamingAssetsRelativePath_StaysInsideRoot()
        {
            AudioClipReference reference = CreateReference(AudioLocationKind.StreamingAssetsPath, "Audio/Clip.wav");

            try
            {
                string expected = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "Audio/Clip.wav"));

                Assert.IsTrue(reference.TryResolveLocation(out string resolvedLocation, out string error));
                Assert.AreEqual(expected, resolvedLocation);
                Assert.AreEqual(string.Empty, error);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void ResolveLocation_StreamingAssetsParentTraversal_ReturnsEmpty()
        {
            AudioClipReference reference = CreateReference(AudioLocationKind.StreamingAssetsPath, "../Outside.wav");

            try
            {
                Assert.IsFalse(reference.TryResolveLocation(out string resolvedLocation, out string error));
                Assert.AreEqual(string.Empty, resolvedLocation);
                Assert.IsNotEmpty(error);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void ResolveLocation_PersistentDataAbsoluteEscape_ReturnsEmpty()
        {
            string outsidePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CycloneGamesAudioOutside.wav"));
            AudioClipReference reference = CreateReference(AudioLocationKind.PersistentDataPath, outsidePath);

            try
            {
                Assert.IsFalse(reference.TryResolveLocation(out string resolvedLocation, out string error));
                Assert.AreEqual(string.Empty, resolvedLocation);
                Assert.IsNotEmpty(error);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [TestCase("https://cdn.example.com/audio/clip.ogg", true)]
        [TestCase("http://localhost/audio/clip.wav", true)]
        [TestCase("ftp://example.com/audio/clip.ogg", false)]
        [TestCase("file:///tmp/audio/clip.ogg", false)]
        [TestCase("audio/clip.ogg", false)]
        public void TryResolveLocation_Url_AcceptsOnlyAbsoluteHttpOrHttps(string location, bool expectedSuccess)
        {
            AudioClipReference reference = CreateReference(AudioLocationKind.Url, location);

            try
            {
                bool success = reference.TryResolveLocation(out string resolvedLocation, out string error);

                Assert.AreEqual(expectedSuccess, success);
                if (expectedSuccess)
                {
                    Assert.AreEqual(location, resolvedLocation);
                    Assert.AreEqual(string.Empty, error);
                }
                else
                {
                    Assert.AreEqual(string.Empty, resolvedLocation);
                    Assert.IsNotEmpty(error);
                }
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("Audio/Bad\0Clip.ogg")]
        public void TryResolveLocation_InvalidLocation_ReturnsError(string location)
        {
            AudioClipReference reference = AudioClipReference.CreateRuntime(AudioLocationKind.FilePath, location);

            try
            {
                Assert.IsFalse(reference.TryResolveLocation(out string resolvedLocation, out string error));
                Assert.AreEqual(string.Empty, resolvedLocation);
                Assert.IsNotEmpty(error);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void TryResolveLocation_OverLengthLocation_ReturnsError()
        {
            AudioClipReference reference = CreateReference(AudioLocationKind.FilePath, new string('a', 4097));

            try
            {
                Assert.IsFalse(reference.TryResolveLocation(out string resolvedLocation, out string error));
                Assert.AreEqual(string.Empty, resolvedLocation);
                StringAssert.Contains("4096", error);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void DefaultAssetReference_IsImmutable()
        {
            AudioClipReference reference = CreateReference(
                AudioLocationKind.AssetAddress,
                "audio/original",
                "original-guid");

            try
            {
                Assert.IsFalse(reference.RuntimeMutable);
                Assert.AreEqual(0, reference.Version);

                Assert.Throws<System.InvalidOperationException>(() => reference.SetLocation("audio/replacement"));
                Assert.Throws<System.InvalidOperationException>(() =>
                    reference.SetAssetLocation("audio/replacement", "replacement-guid"));
                bool changed = reference.TrySetLocation(AudioLocationKind.Url, "https://example.com/replacement.ogg");

                Assert.IsFalse(changed);
                Assert.AreEqual(AudioLocationKind.AssetAddress, reference.LocationKind);
                Assert.AreEqual("audio/original", reference.Location);
                Assert.AreEqual("original-guid", reference.GUID);
                Assert.AreEqual(0, reference.Version);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void CreateRuntime_CreatesMutableCallerOwnedReference()
        {
            AudioClipReference reference = AudioClipReference.CreateRuntime(
                AudioLocationKind.AssetAddress,
                "audio/runtime",
                "runtime-guid");

            try
            {
                Assert.IsTrue(reference.RuntimeMutable);
                Assert.AreEqual(AudioLocationKind.AssetAddress, reference.LocationKind);
                Assert.AreEqual("audio/runtime", reference.Location);
                Assert.AreEqual("runtime-guid", reference.GUID);
                Assert.AreEqual(1, reference.Version);
                Assert.AreEqual(HideFlags.DontSave, reference.hideFlags);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void RuntimeReference_MutationsIncrementVersionOnlyWhenStateChanges()
        {
            AudioClipReference reference = AudioClipReference.CreateRuntime(
                AudioLocationKind.AssetAddress,
                "audio/original",
                "original-guid");

            try
            {
                reference.SetLocation("audio/file");
                Assert.AreEqual(2, reference.Version);
                Assert.AreEqual(string.Empty, reference.GUID);

                reference.SetLocation("audio/file");
                Assert.AreEqual(2, reference.Version);

                reference.SetAssetLocation("audio/address", "address-guid");
                Assert.AreEqual(3, reference.Version);

                reference.SetAssetLocation("audio/address", "address-guid");
                Assert.AreEqual(3, reference.Version);

                Assert.IsTrue(reference.TrySetLocation(
                    AudioLocationKind.Url,
                    "https://example.com/audio/file.ogg"));
                Assert.AreEqual(4, reference.Version);

                Assert.IsTrue(reference.TrySetLocation(
                    AudioLocationKind.Url,
                    "https://example.com/audio/file.ogg"));
                Assert.AreEqual(4, reference.Version);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        [Test]
        public void ResolveLocation_FromWorkerThread_ThrowsMainThreadContractError()
        {
            AudioClipReference reference = AudioClipReference.CreateRuntime(
                AudioLocationKind.AssetAddress,
                "audio/thread-contract");
            System.Exception exception = null;
            var worker = new Thread(() =>
            {
                try
                {
                    reference.ResolveLocation();
                }
                catch (System.Exception caught)
                {
                    exception = caught;
                }
            })
            {
                IsBackground = true
            };

            try
            {
                worker.Start();
                Assert.IsTrue(worker.Join(5000), "Worker thread did not finish within the test timeout.");
                Assert.IsInstanceOf<System.InvalidOperationException>(exception);
                StringAssert.Contains("Unity main thread", exception.Message);
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        private static AudioClipReference CreateReference(
            AudioLocationKind locationKind,
            string location,
            string guid = "")
        {
            AudioClipReference reference = ScriptableObject.CreateInstance<AudioClipReference>();
            SerializedObject serializedObject = new SerializedObject(reference);

            serializedObject.FindProperty("locationKind").enumValueIndex = (int)locationKind;
            serializedObject.FindProperty("m_Location").stringValue = location;
            serializedObject.FindProperty("m_GUID").stringValue = guid;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            return reference;
        }
    }
}
