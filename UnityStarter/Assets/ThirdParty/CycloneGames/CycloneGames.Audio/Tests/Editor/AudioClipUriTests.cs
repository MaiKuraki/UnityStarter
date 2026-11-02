// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.IO;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;

namespace CycloneGames.Audio.Tests
{
    public sealed class AudioClipUriTests
    {
        [Test]
        public void AbsoluteFilePath_BecomesFileUri()
        {
            string path = Path.Combine(Path.GetTempPath(), "audio-probe.mp3");

            Assert.IsTrue(AudioClipUri.TryCreate(path, out string uri, out string error), error);
            Assert.IsTrue(Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed), uri);
            Assert.IsTrue(parsed.IsFile, uri);
            Assert.AreEqual("file", parsed.Scheme);
            StringAssert.EndsWith("audio-probe.mp3", uri);
        }

        [Test]
        public void FilePathWithSpaces_IsEscaped()
        {
            string path = Path.Combine(Path.GetTempPath(), "audio probe.mp3");

            Assert.IsTrue(AudioClipUri.TryCreate(path, out string uri, out string error), error);
            StringAssert.Contains("%20", uri);
            Assert.IsFalse(uri.Contains(" "), "raw spaces must not survive into the request URI");
        }

        [Test]
        public void FilePathWithFragmentCharacter_IsEscaped()
        {
            string path = Path.Combine(Path.GetTempPath(), "audio#probe.mp3");

            Assert.IsTrue(AudioClipUri.TryCreate(path, out string uri, out string error), error);
            StringAssert.Contains("%23", uri);
            Assert.IsFalse(uri.Contains("#"), "a fragment marker must not truncate the request path");
        }

        [Test]
        public void AndroidJarUri_IsPassedThrough()
        {
            const string uri = "jar:file:///data/app/com.example/base.apk!/assets/audio/step.mp3";

            Assert.IsTrue(AudioClipUri.TryCreate(uri, out string result, out string error), error);
            Assert.AreEqual(uri, result);
        }

        [Test]
        public void HttpsUri_IsPassedThrough()
        {
            const string uri = "https://cdn.example.com/audio/step.mp3";

            Assert.IsTrue(AudioClipUri.TryCreate(uri, out string result, out string error), error);
            Assert.AreEqual(uri, result);
        }

        [Test]
        public void FileUri_IsPassedThroughUnchanged()
        {
            const string uri = "file:///C:/audio/step.mp3";

            Assert.IsTrue(AudioClipUri.TryCreate(uri, out string result, out string error), error);
            Assert.AreEqual(uri, result);
        }

        [Test]
        public void RelativePath_IsRejected()
        {
            Assert.IsFalse(AudioClipUri.TryCreate("audio/step.mp3", out string uri, out string error));
            Assert.AreEqual(string.Empty, uri);
            StringAssert.Contains("absolute", error);
        }

        [Test]
        public void EmptyLocation_IsRejected()
        {
            Assert.IsFalse(AudioClipUri.TryCreate(null, out _, out _));
            Assert.IsFalse(AudioClipUri.TryCreate(string.Empty, out _, out _));
            Assert.IsFalse(AudioClipUri.TryCreate("   ", out _, out _));
        }

        [Test]
        public void WindowsDriveAndUncPaths_ProduceFileUris()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Ignore("Windows-only path shapes.");
            }

            Assert.IsTrue(AudioClipUri.TryCreate(@"E:\audio\step.mp3", out string driveUri, out string driveError), driveError);
            StringAssert.StartsWith("file:///E:/audio/step.mp3", driveUri);

            Assert.IsTrue(AudioClipUri.TryCreate(@"\\server\share\step.mp3", out string uncUri, out string uncError), uncError);
            StringAssert.StartsWith("file://server/share/step.mp3", uncUri);
        }
    }
}
