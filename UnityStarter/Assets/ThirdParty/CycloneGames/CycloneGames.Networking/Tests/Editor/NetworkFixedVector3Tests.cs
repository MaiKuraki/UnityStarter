using CycloneGames.Networking.Buffers;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkFixedVector3Tests
    {
        [Test]
        public void FixedVector_RoundTrips_Exactly()
        {
            var original = new NetworkVector3(123.456f, -0.001f, 99999.5f);
            NetworkFixedVector3 fixedVector = NetworkFixedVector3.FromNetworkVector3(original);

            NetworkBuffer buffer = NetworkBufferPool.Get();
            try
            {
                fixedVector.WriteTo(buffer);
                buffer.FlipForRead();
                NetworkFixedVector3 decoded = NetworkFixedVector3.ReadFrom(buffer);

                Assert.AreEqual(fixedVector, decoded);
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [Test]
        public void FixedVector_UsesIntegerRawValues_CrossPlatformStable()
        {
            NetworkFixedVector3 a = NetworkFixedVector3.FromNetworkVector3(new NetworkVector3(1.5f, 2.5f, -3.25f));
            NetworkFixedVector3 b = NetworkFixedVector3.FromNetworkVector3(new NetworkVector3(1.5f, 2.5f, -3.25f));

            Assert.AreEqual(a.X.RawValue, b.X.RawValue);
            Assert.AreEqual(a.Y.RawValue, b.Y.RawValue);
            Assert.AreEqual(a.Z.RawValue, b.Z.RawValue);
            Assert.AreEqual(1.5f, a.X.ToFloat(), 1e-6f);
        }

        [Test]
        public void QuantizedVector_RoundTrips_WithinGridResolution()
        {
            var original = new NetworkVector3(10.0004f, -2.9996f, 0.5f);

            NetworkBuffer buffer = NetworkBufferPool.Get();
            try
            {
                NetworkVector3Quantization.WriteQuantized(buffer, original);
                buffer.FlipForRead();
                NetworkVector3 decoded = NetworkVector3Quantization.ReadQuantized(buffer);

                Assert.AreEqual(10.0f, decoded.X, 0.001f);
                Assert.AreEqual(-3.0f, decoded.Y, 0.001f);
                Assert.AreEqual(0.5f, decoded.Z, 0.001f);
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [Test]
        public void QuantizedVector_SaturatesBeyondInt32Grid()
        {
            var extreme = new NetworkVector3(3e10f, -3e10f, 0f);

            NetworkBuffer buffer = NetworkBufferPool.Get();
            try
            {
                NetworkVector3Quantization.WriteQuantized(buffer, extreme);
                buffer.FlipForRead();
                NetworkVector3 decoded = NetworkVector3Quantization.ReadQuantized(buffer);

                Assert.IsTrue(float.IsFinite(decoded.X));
                Assert.IsTrue(float.IsFinite(decoded.Y));
                Assert.Greater(decoded.X, 0f);
                Assert.Less(decoded.Y, 0f);
            }
            finally
            {
                buffer.Dispose();
            }
        }
    }
}
