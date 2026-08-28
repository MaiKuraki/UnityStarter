using System;
using System.IO;
using NUnit.Framework;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// Pure-logic tests for AtlasImageInfo. The class depends only on System.IO and never touches
    /// AssetDatabase, so it runs outside the Unity asset pipeline.
    /// </summary>
    [TestFixture]
    public sealed class AtlasImageInfoTests
    {
        private const int MinimumFileLength = 24;

        private string _tempDirectory;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "xiang_atlas_image_info_tests");
            Directory.CreateDirectory(_tempDirectory);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        // --------------------------------------------------------------------
        // BUG-001 regression
        // --------------------------------------------------------------------

        [Test]
        public void TryReadSize_Jpeg_ReadsWidthAndHeightFromCorrectOffsets()
        {
            // Regression for BUG-001: the SOF segment offsets were shifted by 2 bytes, so height
            // read Width and width read (Nf<<8)|componentId. Before the fix this 100x3000 image
            // returned (769, 100).
            string path = WriteTempFile(
                "baseline_100x3000.jpg",
                BuildJpeg(width: 100, height: 3000, componentCount: 3));

            Assert.IsTrue(
                AtlasImageInfo.TryReadSize(path, out int width, out int height),
                "Synthetic JPEG should parse successfully.");
            Assert.AreEqual(100, width, "Width must come from SOF offsets 3..4.");
            Assert.AreEqual(3000, height, "Height must come from SOF offsets 1..2.");
        }

        [TestCase(1, 257)]
        [TestCase(3, 769)]
        [TestCase(4, 1025)]
        public void TryReadSize_Jpeg_WidthIsNotPollutedByComponentCount(
            int componentCount,
            int buggyWidth)
        {
            // Before the fix, width read (Nf<<8)|first component id, degenerating into a constant
            // determined only by component count: grayscale 257 / YCbCr 769 / CMYK 1025.
            string path = WriteTempFile(
                $"nf_{componentCount}.jpg",
                BuildJpeg(width: 512, height: 256, componentCount: componentCount));

            Assert.IsTrue(
                AtlasImageInfo.TryReadSize(path, out int width, out int height));
            Assert.AreEqual(512, width);
            Assert.AreEqual(256, height);
            Assert.AreNotEqual(
                buggyWidth,
                width,
                "Width must not be polluted by the component count.");
        }

        [Test]
        public void TryReadSize_Jpeg_VerticalImage_HeightIsNotLost()
        {
            // A vertical image is the classic false-negative case: once height is lost,
            // CheckTextureSize's Mathf.Max no longer sees the true long edge.
            string path = WriteTempFile(
                "vertical.jpg",
                BuildJpeg(width: 300, height: 4096, componentCount: 3));

            Assert.IsTrue(
                AtlasImageInfo.TryReadSize(path, out int width, out int height));
            Assert.AreEqual(300, width);
            Assert.AreEqual(4096, height);
        }

        // --------------------------------------------------------------------
        // PNG control group (the PNG branch was already correct; lock it so it stays that way)
        // --------------------------------------------------------------------

        [Test]
        public void TryReadSize_Png_ReadsDimensions()
        {
            string path = WriteTempFile("control_640x480.png", BuildPng(640, 480));

            Assert.IsTrue(
                AtlasImageInfo.TryReadSize(path, out int width, out int height));
            Assert.AreEqual(640, width);
            Assert.AreEqual(480, height);
        }

        [Test]
        public void TryReadSize_Png_HandlesLargeDimension()
        {
            string path = WriteTempFile("large.png", BuildPng(4096, 4096));

            Assert.IsTrue(
                AtlasImageInfo.TryReadSize(path, out int width, out int height));
            Assert.AreEqual(4096, width);
            Assert.AreEqual(4096, height);
        }

        // --------------------------------------------------------------------
        // Negative paths
        // --------------------------------------------------------------------

        [Test]
        public void TryReadSize_FileShorterThanHeader_ReturnsFalse()
        {
            string path = WriteTempFile("short.png", new byte[MinimumFileLength - 1]);

            Assert.IsFalse(
                AtlasImageInfo.TryReadSize(path, out int width, out int height));
            Assert.AreEqual(0, width);
            Assert.AreEqual(0, height);
        }

        [Test]
        public void TryReadSize_UnknownFormat_ReturnsFalse()
        {
            var bytes = new byte[64];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)'A';
            }

            string path = WriteTempFile("not_an_image.bin", bytes);

            Assert.IsFalse(
                AtlasImageInfo.TryReadSize(path, out _, out _));
        }

        [Test]
        public void TryReadSize_JpegWithoutSof_ReturnsFalse()
        {
            // Only SOI + COM + EOI; an SOF segment is never reached.
            var bytes = new byte[MinimumFileLength + 8];
            bytes[0] = 0xFF;
            bytes[1] = 0xD8;
            bytes[bytes.Length - 2] = 0xFF;
            bytes[bytes.Length - 1] = 0xD9;

            string path = WriteTempFile("no_sof.jpg", bytes);

            Assert.IsFalse(
                AtlasImageInfo.TryReadSize(path, out _, out _));
        }

        [Test]
        public void TryReadSize_MissingFile_ReturnsFalse()
        {
            Assert.IsFalse(AtlasImageInfo.TryReadSize(
                Path.Combine(_tempDirectory, "does_not_exist.png"),
                out _,
                out _));
        }

        // --------------------------------------------------------------------
        // Synthetic file builders
        // --------------------------------------------------------------------

        private string WriteTempFile(string fileName, byte[] bytes)
        {
            string path = Path.Combine(_tempDirectory, fileName);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        /// <summary>
        /// Builds a minimal baseline JPEG: SOI + COM (padding) + SOF0 + EOI. The file must be at
        /// least 24 bytes, otherwise TryReadSize's header pre-read check returns false immediately.
        /// </summary>
        private static byte[] BuildJpeg(int width, int height, int componentCount)
        {
            const int CommentPayload = 10;
            const int CommentSegmentLength = 2 + CommentPayload;

            // SOF payload: precision(1) + height(2) + width(2) + Nf(1) + 3 bytes per component
            int sofPayload = 6 + (componentCount * 3);
            int sofSegmentLength = 2 + sofPayload;

            int total = 2
                        + (2 + CommentSegmentLength)
                        + (2 + sofSegmentLength)
                        + 2;

            var bytes = new byte[total];
            int i = 0;

            bytes[i++] = 0xFF;
            bytes[i++] = 0xD8;

            bytes[i++] = 0xFF;
            bytes[i++] = 0xFE;
            bytes[i++] = (byte)(CommentSegmentLength >> 8);
            bytes[i++] = (byte)(CommentSegmentLength & 0xFF);
            for (int c = 0; c < CommentPayload; c++)
            {
                bytes[i++] = 0x20;
            }

            bytes[i++] = 0xFF;
            bytes[i++] = 0xC0;
            bytes[i++] = (byte)(sofSegmentLength >> 8);
            bytes[i++] = (byte)(sofSegmentLength & 0xFF);
            bytes[i++] = 0x08;
            bytes[i++] = (byte)(height >> 8);
            bytes[i++] = (byte)(height & 0xFF);
            bytes[i++] = (byte)(width >> 8);
            bytes[i++] = (byte)(width & 0xFF);
            bytes[i++] = (byte)componentCount;
            for (int c = 0; c < componentCount; c++)
            {
                bytes[i++] = (byte)(c + 1);
                bytes[i++] = 0x11;
                bytes[i++] = 0x00;
            }

            bytes[i++] = 0xFF;
            bytes[i++] = 0xD9;
            return bytes;
        }

        /// <summary>
        /// Builds a minimal PNG: signature + IHDR. The CRC is not parsed, so it is left zero.
        /// </summary>
        private static byte[] BuildPng(int width, int height)
        {
            var bytes = new byte[33];
            int i = 0;

            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            for (int s = 0; s < signature.Length; s++)
            {
                bytes[i++] = signature[s];
            }

            bytes[i++] = 0x00;
            bytes[i++] = 0x00;
            bytes[i++] = 0x00;
            bytes[i++] = 0x0D;

            bytes[i++] = (byte)'I';
            bytes[i++] = (byte)'H';
            bytes[i++] = (byte)'D';
            bytes[i++] = (byte)'R';

            WriteBigEndianInt32(bytes, ref i, width);
            WriteBigEndianInt32(bytes, ref i, height);

            bytes[i++] = 0x08;
            bytes[i++] = 0x06;
            bytes[i++] = 0x00;
            bytes[i++] = 0x00;
            bytes[i++] = 0x00;

            bytes[i++] = 0x00;
            bytes[i++] = 0x00;
            bytes[i++] = 0x00;
            bytes[i++] = 0x00;
            return bytes;
        }

        private static void WriteBigEndianInt32(byte[] bytes, ref int index, int value)
        {
            bytes[index++] = (byte)((value >> 24) & 0xFF);
            bytes[index++] = (byte)((value >> 16) & 0xFF);
            bytes[index++] = (byte)((value >> 8) & 0xFF);
            bytes[index++] = (byte)(value & 0xFF);
        }
    }
}
