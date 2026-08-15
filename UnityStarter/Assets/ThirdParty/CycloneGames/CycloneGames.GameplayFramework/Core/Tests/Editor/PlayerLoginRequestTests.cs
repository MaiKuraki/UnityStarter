using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Core.Tests
{
    public sealed class PlayerLoginRequestTests
    {
        [Test]
        public void ValidRequest_PassesBoundedValidation()
        {
            var request = new PlayerLoginRequest(
                7,
                "Player",
                remoteAddress: "127.0.0.1",
                options: "team=blue");

            Assert.IsTrue(request.TryValidate(out string error));
            Assert.IsNull(error);
        }

        [Test]
        public void InvalidBoundaries_AreRejected()
        {
            Assert.IsFalse(new PlayerLoginRequest(-1, "Player").TryValidate(out _));
            Assert.IsFalse(new PlayerLoginRequest(
                1,
                new string('x', PlayerLoginRequest.MaxPlayerNameLength + 1)).TryValidate(out _));
            Assert.IsFalse(new PlayerLoginRequest(
                1,
                "Local",
                remoteAddress: "127.0.0.1",
                isLocal: true).TryValidate(out _));
        }

        [Test]
        public void RuntimeSnapshot_HasNoTransportSchemaState()
        {
            var snapshot = new PlayerStateSnapshot("Player", 42, isSpectator: true);

            Assert.AreEqual("Player", snapshot.PlayerName);
            Assert.AreEqual(42, snapshot.PlayerId);
            Assert.IsTrue(snapshot.IsSpectator);
            Assert.IsTrue(snapshot.TryValidate(out _));

            Assert.IsFalse(
                new PlayerStateSnapshot("Player", -1, isSpectator: false).TryValidate(
                    out PlayerStateSnapshotValidationResult invalidId));
            Assert.AreEqual(PlayerStateSnapshotValidationResult.InvalidPlayerId, invalidId);
        }

        [Test]
        public void ActorTagValidation_UsesBoundedAllocationFreeResults()
        {
            Assert.IsFalse(ActorTagLimits.TryValidate(null, out ActorTagValidationResult nullResult));
            Assert.AreEqual(ActorTagValidationResult.NullOrWhiteSpace, nullResult);
            Assert.IsFalse(ActorTagLimits.TryValidate(
                new string('x', ActorTagLimits.MaximumTagLength + 1),
                out ActorTagValidationResult lengthResult));
            Assert.AreEqual(ActorTagValidationResult.TooLong, lengthResult);
            Assert.IsTrue(ActorTagLimits.TryValidate("Gameplay.Player", out _));
        }
    }
}
