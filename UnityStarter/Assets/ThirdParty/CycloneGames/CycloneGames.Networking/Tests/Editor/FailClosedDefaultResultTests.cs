using CycloneGames.Networking.Authentication;
using CycloneGames.Networking.Platform;
using CycloneGames.Networking.Security;
using CycloneGames.Networking.Simulation;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class FailClosedDefaultResultTests
    {
        [Test]
        public void DefaultProcessResults_AreInvalidAndNeverSuccessful()
        {
            Assert.AreEqual(NetworkHandshakeResult.Invalid, default(NetworkHandshakeResult));
            Assert.AreEqual(ValidationResult.Invalid, default(ValidationResult));
            Assert.AreEqual(NetworkFrameResult.Invalid, default(NetworkFrameResult));
            Assert.AreEqual(MessageSecurityResult.Invalid, default(MessageSecurityResult));

            NetworkSecurityPipelineResult security = default;
            Assert.IsFalse(security.Accepted);

            NetworkAuthenticationResult authentication = default;
            Assert.AreEqual(NetworkAuthenticationStatus.Invalid, authentication.Status);
            Assert.IsFalse(authentication.IsAccepted);

            NetworkSendResult send = default;
            Assert.AreEqual(NetworkSendStatus.Invalid, send.Status);
            Assert.IsFalse(send.Succeeded);
            Assert.IsTrue(send.Failed);

            NetworkAuthorityValidationResult authority = default;
            Assert.AreEqual(NetworkAuthorityValidationStatus.Invalid, authority.Status);
            Assert.IsFalse(authority.IsAccepted);

            NetworkActionResult action = default;
            Assert.AreEqual(NetworkActionResultCode.Invalid, action.Code);
            Assert.IsFalse(action.IsAccepted);

            NetworkHostPermissionRequestResult permission = default;
            Assert.AreEqual(NetworkHostPermissionRequestOutcome.Unknown, permission.Outcome);
            Assert.IsFalse(permission.Launched);
        }
    }
}
