using System.Text;

using NUnit.Framework;

namespace CycloneGames.IO.Tests.Core
{
    public sealed class TextCodecTests
    {
        [Test]
        public void Decode_Utf8Bom_StripsPreamble()
        {
            byte[] content = { 0xEF, 0xBB, 0xBF, 0x43, 0x79, 0x63, 0x6C, 0x6F, 0x6E, 0x65 };

            Assert.That(TextCodec.Decode(content), Is.EqualTo("Cyclone"));
        }

        [Test]
        public void Decode_InvalidUtf8_Throws()
        {
            byte[] content = { 0xC3, 0x28 };

            Assert.Throws<DecoderFallbackException>(() => TextCodec.Decode(content));
        }

        [Test]
        public void Decode_BomlessUtf16_DoesNotGuessEncoding()
        {
            byte[] content = { 0x41, 0x00 };

            Assert.That(TextCodec.Decode(content), Is.EqualTo("A\0"));
        }

        [Test]
        public void Encode_DefaultUtf8_DoesNotAddPreamble()
        {
            byte[] content = TextCodec.Encode("Cyclone");

            Assert.That(content, Is.EqualTo(Encoding.UTF8.GetBytes("Cyclone")));
        }

        [Test]
        public void Encode_Utf16WithPreamble_RoundTripsDeterministically()
        {
            byte[] content = TextCodec.Encode("Cyclone", Encoding.Unicode, true);

            Assert.That(content[0], Is.EqualTo(0xFF));
            Assert.That(content[1], Is.EqualTo(0xFE));
            Assert.That(TextCodec.Decode(content), Is.EqualTo("Cyclone"));
        }

        [Test]
        public void Encode_UnpairedSurrogate_Throws()
        {
            Assert.Throws<EncoderFallbackException>(() => TextCodec.Encode("\uD800"));
        }
    }
}
