using CycloneGames.AssetManagement.Runtime.Cache;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetMemoryEstimatorTests
    {
        [Test]
        public void Texture_Rgba32_UsesFourBytesPerPixel()
        {
            Assert.That(AssetMemoryEstimator.EstimateTexture(8, 8, 1, GraphicsFormat.R8G8B8A8_UNorm), Is.EqualTo(256L));
        }

        [Test]
        public void Texture_Rgba32_MipChainAddsOneThird()
        {
            Assert.That(AssetMemoryEstimator.EstimateTexture(8, 8, 2, GraphicsFormat.R8G8B8A8_UNorm), Is.EqualTo(341L));
        }

        [Test]
        public void Texture_HdrHalf_UsesEightBytesPerPixel()
        {
            Assert.That(AssetMemoryEstimator.EstimateTexture(8, 8, 1, GraphicsFormat.R16G16B16A16_SFloat), Is.EqualTo(512L));
        }

        [Test]
        public void Texture_CompressedBc7_UsesBlockFootprint()
        {
            // 8x8 BC7 = 2x2 blocks of 16 bytes.
            Assert.That(AssetMemoryEstimator.EstimateTexture(8, 8, 1, GraphicsFormat.RGBA_BC7_UNorm), Is.EqualTo(64L));
        }

        [Test]
        public void Texture_UnknownFormat_FallsBackToRgba32Equivalent()
        {
            Assert.That(AssetMemoryEstimator.EstimateTexture(8, 8, 1, GraphicsFormat.None), Is.EqualTo(256L));
        }

        [Test]
        public void Texture_CompressedNeverExceedsRgba32Equivalent()
        {
            long compressed = AssetMemoryEstimator.EstimateTexture(64, 64, 1, GraphicsFormat.RGBA_BC7_UNorm);
            long rgba32 = AssetMemoryEstimator.EstimateTexture(64, 64, 1, GraphicsFormat.R8G8B8A8_UNorm);

            Assert.That(compressed, Is.LessThan(rgba32));
        }

        [Test]
        public void Texture_HdrNeverUnderestimatesRgba32Equivalent()
        {
            long half = AssetMemoryEstimator.EstimateTexture(8, 8, 1, GraphicsFormat.R16G16B16A16_SFloat);
            long rgba32 = AssetMemoryEstimator.EstimateTexture(8, 8, 1, GraphicsFormat.R8G8B8A8_UNorm);

            Assert.That(half, Is.GreaterThan(rgba32));
        }
    }
}
