using CycloneGames.Networking.Security;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class MessageValidatorTests
    {
        [Test]
        public void ValidateBuffer_Rejects_Overflowing_Range()
        {
            var validator = new MessageValidator(maxPayloadSize: 16, minPayloadSize: 1);
            byte[] buffer = new byte[8];

            Assert.IsFalse(validator.ValidateBuffer(buffer, int.MaxValue, 2));
            Assert.IsFalse(validator.ValidateBuffer(buffer, 7, 2));
        }

        [Test]
        public void Validate_Returns_Expected_Size_Results()
        {
            var validator = new MessageValidator(maxPayloadSize: 8, minPayloadSize: 2);

            Assert.AreEqual(ValidationResult.PayloadTooSmall, validator.Validate(1, 1));
            Assert.AreEqual(ValidationResult.PayloadTooLarge, validator.Validate(1, 9));
            Assert.AreEqual(ValidationResult.Valid, validator.Validate(1, 8));
        }
    }
}
