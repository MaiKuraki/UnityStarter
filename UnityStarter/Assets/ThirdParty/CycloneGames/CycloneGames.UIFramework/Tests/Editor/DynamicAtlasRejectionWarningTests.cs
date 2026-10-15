using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CycloneGames.Logging;
using CycloneGames.UIFramework.DynamicAtlas;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class DynamicAtlasRejectionWarningTests
    {
        [TestCase(true, SpritePackingMode.Tight, SpritePackingRotation.None, true)]
        [TestCase(true, SpritePackingMode.Rectangle, SpritePackingRotation.Any, true)]
        [TestCase(true, SpritePackingMode.Tight, SpritePackingRotation.Any, true)]
        [TestCase(true, SpritePackingMode.Rectangle, SpritePackingRotation.None, false)]
        [TestCase(false, SpritePackingMode.Tight, SpritePackingRotation.Any, false)]
        [TestCase(false, SpritePackingMode.Rectangle, SpritePackingRotation.None, false)]
        public void UnsupportedPackingBranch_RequiresPackedAtlasWithRotationOrTightPacking(
            bool packed,
            SpritePackingMode packingMode,
            SpritePackingRotation packingRotation,
            bool expected)
        {
            MethodInfo predicate = typeof(DynamicAtlasService).GetMethod(
                "IsUnsupportedPacking",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(predicate, Is.Not.Null);

            object result = predicate.Invoke(
                null,
                new object[] { packed, packingMode, packingRotation });

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void PackingRejectionWarning_WarnsOncePerKey_AndAgainAfterClear()
        {
            const string reason = "the source atlas uses tight packing";
            const string guidance =
                "Disable rotation and tight packing on the source SpriteAtlas, or mark its import rule for runtime extraction.";
            var recorder = new RecordingLogWriter();
            ILogWriter previousWriter = InstallRecorder(recorder);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                // EditMode cannot fabricate a packed sprite, so the warning hook is invoked
                // directly with the same reason and guidance the packing branch passes.
                TestReflection.Invoke(service, "WarnRejectionOnce", "shout/tight", reason, guidance);
                TestReflection.Invoke(service, "WarnRejectionOnce", "shout/tight", reason, guidance);

                Assert.That(recorder.Warnings.Count, Is.EqualTo(1));
                Assert.That(recorder.Warnings[0], Does.Contain("shout/tight"));
                Assert.That(recorder.Warnings[0], Does.Contain("tight packing"));
                Assert.That(recorder.Warnings[0], Does.Contain("import rule"));

                TestReflection.Invoke(service, "WarnRejectionOnce", "shout/other", reason, guidance);
                Assert.That(recorder.Warnings.Count, Is.EqualTo(2));

                service.Clear();
                TestReflection.Invoke(service, "WarnRejectionOnce", "shout/tight", reason, guidance);
                Assert.That(
                    recorder.Warnings.Count,
                    Is.EqualTo(3),
                    "Clear() must reset the per-key warning deduplication.");
            }
            finally
            {
                RestoreRecorder(recorder, previousWriter);
            }
        }

        [Test]
        public void CopyUnsupported_WarnsOnceWithFormatReasonAndGuidance()
        {
            Texture2D incompatible = CreateTexture(8, 8, TextureFormat.ARGB32, Color.white);
            var recorder = new RecordingLogWriter();
            ILogWriter previousWriter = InstallRecorder(recorder);
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.copyFallback = DynamicAtlasCopyFallback.GpuOnly;
            using var service = new DynamicAtlasService(config);

            try
            {
                DynamicAtlasInsertStatus first = service.TryAcquire("shout/format", incompatible, out _);
                DynamicAtlasInsertStatus second = service.TryAcquire("shout/format", incompatible, out _);

                Assert.That(first, Is.EqualTo(DynamicAtlasInsertStatus.CopyUnsupported));
                Assert.That(second, Is.EqualTo(DynamicAtlasInsertStatus.CopyUnsupported));
                Assert.That(recorder.Warnings.Count, Is.EqualTo(1));
                Assert.That(recorder.Warnings[0], Does.Contain("shout/format"));
                Assert.That(recorder.Warnings[0], Does.Contain("cannot be copied"));
                Assert.That(recorder.Warnings[0], Does.Contain("import rule"));
            }
            finally
            {
                RestoreRecorder(recorder, previousWriter);
                Destroy(incompatible);
            }
        }

        [Test]
        public void SuccessfulAcquireAndCacheHit_ProduceNoRejectionWarnings()
        {
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            var recorder = new RecordingLogWriter();
            ILogWriter previousWriter = InstallRecorder(recorder);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                Assert.That(
                    service.TryAcquire("quiet/success", source, out DynamicAtlasSpriteLease lease),
                    Is.EqualTo(DynamicAtlasInsertStatus.Success));
                Assert.That(
                    service.TryAcquire("quiet/success", source, out DynamicAtlasSpriteLease cached),
                    Is.EqualTo(DynamicAtlasInsertStatus.CacheHit));
                Assert.That(service.TryGetSprite("quiet/success", out _), Is.True);

                Assert.That(recorder.Warnings.Count, Is.Zero);
                cached.Dispose();
                lease.Dispose();
            }
            finally
            {
                RestoreRecorder(recorder, previousWriter);
                Destroy(source);
            }
        }

        private static ILogWriter InstallRecorder(RecordingLogWriter recorder)
        {
            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.That(
                LogRuntime.TryReplaceWriter(previousWriter, recorder),
                Is.True,
                "Failed to install the recording log writer.");
            return previousWriter;
        }

        private static void RestoreRecorder(RecordingLogWriter recorder, ILogWriter previousWriter)
        {
            LogRuntime.TryReplaceWriter(recorder, previousWriter);
        }

        private static Texture2D CreateTexture(int width, int height, TextureFormat format, Color color)
        {
            var texture = new Texture2D(width, height, format, mipChain: false);
            var pixels = new Color32[width * height];
            Color32 pixel = color;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = pixel;
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }

        private static void Destroy(UnityEngine.Object target)
        {
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private sealed class RecordingLogWriter : ILogWriter
        {
            internal readonly List<string> Warnings = new List<string>(4);

            public bool IsEnabled(LogSeverity severity, string category)
            {
                return true;
            }

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (severity == LogSeverity.Warning)
                {
                    Warnings.Add(message);
                }
            }

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                RecordBuilder(severity, messageBuilder);
            }

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (severity != LogSeverity.Warning)
                {
                    return;
                }

                var builder = new StringBuilder(128);
                messageBuilder(state, builder);
                Warnings.Add(builder.ToString());
            }

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (severity == LogSeverity.Warning)
                {
                    Warnings.Add(message ?? exception.Message);
                }
            }

            private void RecordBuilder(LogSeverity severity, Action<StringBuilder> messageBuilder)
            {
                if (severity != LogSeverity.Warning)
                {
                    return;
                }

                var builder = new StringBuilder(128);
                messageBuilder(builder);
                Warnings.Add(builder.ToString());
            }
        }
    }
}
