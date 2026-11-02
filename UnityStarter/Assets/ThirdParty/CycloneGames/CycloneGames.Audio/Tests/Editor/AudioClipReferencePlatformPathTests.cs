// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System.Reflection;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Audio.Tests
{
    /// <summary>
    /// Covers the URI-like StreamingAssets branch (Android jar roots, WebGL URLs) and the version
    /// wrap, which the filesystem-only tests cannot reach.
    /// </summary>
    public sealed class AudioClipReferencePlatformPathTests
    {
        private const string AndroidRoot = "jar:file:///data/app/com.example.game/base.apk!/assets";
        private const string WebGlRoot = "https://cdn.example.com/streamingassets";

        [Test]
        public void UriRoot_CombinesSafeRelativePath()
        {
            Assert.IsTrue(TryResolve(AndroidRoot, "audio/step_01.mp3", out string androidPath));
            Assert.AreEqual(AndroidRoot + "/audio/step_01.mp3", androidPath);

            Assert.IsTrue(TryResolve(WebGlRoot, "audio\\step_01.mp3", out string webGlPath));
            Assert.AreEqual(WebGlRoot + "/audio/step_01.mp3", webGlPath);
        }

        [Test]
        public void UriRoot_RejectsParentTraversal()
        {
            Assert.IsFalse(TryResolve(AndroidRoot, "../secret.mp3", out _));
            Assert.IsFalse(TryResolve(AndroidRoot, "audio/../../secret.mp3", out _));
            Assert.IsFalse(TryResolve(WebGlRoot, "..\\secret.mp3", out _));
        }

        [Test]
        public void UriRoot_RejectsQueryFragmentAndPercentEncoding()
        {
            Assert.IsFalse(TryResolve(WebGlRoot, "audio/step.mp3?token=1", out _));
            Assert.IsFalse(TryResolve(WebGlRoot, "audio/step.mp3#fragment", out _));
            Assert.IsFalse(TryResolve(WebGlRoot, "audio/%2e%2e/step.mp3", out _));
        }

        [Test]
        public void UriRoot_RejectsRootedLocation()
        {
            Assert.IsFalse(TryResolve(AndroidRoot, "/etc/passwd", out _));
            Assert.IsFalse(TryResolve(WebGlRoot, "/audio/step.mp3", out _));
        }

        [Test]
        public void UriRoot_RejectsEmptyRootOrLocation()
        {
            Assert.IsFalse(TryResolve(string.Empty, "audio/step.mp3", out _));
            Assert.IsFalse(TryResolve(AndroidRoot, string.Empty, out _));
            Assert.IsFalse(TryResolve(AndroidRoot, "   ", out _));
        }

        [Test]
        public void Version_WrapsToNonZero()
        {
            AudioClipReference reference = AudioClipReference.CreateRuntime(
                AudioLocationKind.StreamingAssetsPath,
                "audio/first.mp3");

            try
            {
                FieldInfo versionField = typeof(AudioClipReference).GetField(
                    "version",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(versionField, "AudioClipReference.version layout changed.");

                versionField.SetValue(reference, int.MaxValue);

                Assert.IsTrue(reference.TrySetLocation(AudioLocationKind.StreamingAssetsPath, "audio/second.mp3"));
                Assert.AreEqual(1, (int)versionField.GetValue(reference), "version must wrap to 1, never to 0");
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }

        private static bool TryResolve(string root, string location, out string resolved)
        {
            MethodInfo method = typeof(AudioClipReference).GetMethod(
                "TryResolveRootedLocation",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "AudioClipReference.TryResolveRootedLocation layout changed.");

            object[] arguments = { root, location, null };
            bool success = (bool)method.Invoke(null, arguments);
            resolved = success ? (string)arguments[2] : string.Empty;
            return success;
        }
    }
}
