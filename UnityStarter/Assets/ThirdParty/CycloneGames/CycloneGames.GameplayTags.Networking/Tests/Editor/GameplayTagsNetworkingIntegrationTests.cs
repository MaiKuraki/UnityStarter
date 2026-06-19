using CycloneGames.GameplayTags.Core;
using CycloneGames.Networking;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Networking.Tests.Editor
{
   public sealed class GameplayTagsNetworkingIntegrationTests
   {
      private static int s_TagPrefixSeed;

      [SetUp]
      public void SetUp()
      {
         GameplayTagRedirector.ClearAll();
         GameplayTagRuntimePlatform.LogWarning = static _ => { };
         GameplayTagRuntimePlatform.LogError = static _ => { };
         GameplayTagRuntimePlatform.IsRuntimePlaying = static () => false;
         GameplayTagRuntimePlatform.LoadBuildTagData = static () => null;
         GameplayTagRuntimePlatform.EnumerateProjectTagSources = static () => System.Array.Empty<IGameplayTagSource>();
         GameplayTagRuntimePlatform.ClearRegisteredProjectTagSources();
      }

      [TearDown]
      public void TearDown()
      {
         GameplayTagRedirector.ClearAll();
         GameplayTagRuntimePlatform.ClearRegisteredProjectTagSources();
      }

      [Test]
      public void Protocol_RegisterMessageCatalog_UsesGameplayTagsRange()
      {
         var catalog = new NetworkMessageCatalog();

         GameplayTagsNetworkProtocol.RegisterMessageCatalog(catalog);

         Assert.IsTrue(catalog.TryGet(
            GameplayTagsNetworkProtocol.MsgManifestHandshake,
            out NetworkMessageDescriptor descriptor));
         Assert.IsTrue(GameplayTagsNetworkProtocol.MessageRange.Contains(descriptor.MessageId));
         Assert.IsTrue(NetworkMessageRanges.Module.Contains(descriptor.MessageId));
         Assert.IsFalse(NetworkMessageRanges.Rpc.Contains(descriptor.MessageId));
         Assert.IsTrue(catalog.TryGetRegisteredModuleRange(descriptor.MessageId, out NetworkMessageIdRange range));
         Assert.AreEqual(GameplayTagsNetworkProtocol.MessageOwner, range.Name);
         Assert.AreEqual(GameplayTagsNetworkProtocol.MessageOwner, descriptor.Owner);
         Assert.AreEqual(NetworkMessageKind.Module, descriptor.Kind);
         Assert.AreEqual(NetworkChannel.Reliable, descriptor.DefaultChannel);
      }

      [Test]
      public void Protocol_RegisterMessageCatalog_IsIdempotentForSameDescriptors()
      {
         var catalog = new NetworkMessageCatalog();

         GameplayTagsNetworkProtocol.RegisterMessageCatalog(catalog);
         GameplayTagsNetworkProtocol.RegisterMessageCatalog(catalog);

         Assert.AreEqual(4, catalog.Count);
      }

      [Test]
      public void Protocol_RegisterMessage_RejectsMessageIdOutsideGameplayTagsRange()
      {
         var catalog = new NetworkMessageCatalog();

         Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            GameplayTagsNetworkProtocol.RegisterMessage<GameplayTagPayloadMessage>(
               catalog,
               NetworkConstants.UserMsgIdMin));
      }

      [Test]
      public void PayloadFactories_ValidateFullAndDeltaPacketKinds()
      {
         string prefix = RegisterTestTags();
         GameplayTag damageFire = GameplayTagManager.RequestTag(prefix + ".Ability.Damage.Fire");
         GameplayTag statusStun = GameplayTagManager.RequestTag(prefix + ".Status.Stun");

         GameplayTagContainer previous = new();
         previous.AddTag(damageFire);

         GameplayTagContainer current = previous.Clone();
         current.AddTag(statusStun);

         byte[] fullPayload = GameplayTagNetSerializer.SerializeFull(current);
         byte[] deltaPayload = GameplayTagNetSerializer.SerializeDelta(current, previous);

         Assert.IsTrue(GameplayTagNetSerializer.IsFullPacket(fullPayload));
         Assert.IsTrue(GameplayTagNetSerializer.IsDeltaPacket(deltaPayload));

         GameplayTagPayloadMessage fullMessage = GameplayTagsNetworkProtocol.CreateFullStateMessage(10u, fullPayload, 7);
         GameplayTagPayloadMessage deltaMessage = GameplayTagsNetworkProtocol.CreateDeltaMessage(10u, deltaPayload, 8);

         Assert.IsTrue(fullMessage.IsValid);
         Assert.IsTrue(deltaMessage.IsValid);
         Assert.AreEqual(GameplayTagNetworkPayloadKind.Full, fullMessage.PayloadKind);
         Assert.AreEqual(GameplayTagNetworkPayloadKind.Delta, deltaMessage.PayloadKind);
         Assert.AreEqual(7, fullMessage.Sequence);
         Assert.AreEqual(8, deltaMessage.Sequence);
         Assert.AreEqual(GameplayTagManager.CurrentManifestHash, fullMessage.ManifestHash);
      }

      [Test]
      public void PayloadFactories_RejectMismatchedPacketKind()
      {
         RegisterTestTags();
         GameplayTagContainer container = new();
         byte[] fullPayload = GameplayTagNetSerializer.SerializeFull(container);

         Assert.Throws<System.ArgumentException>(() =>
            GameplayTagsNetworkProtocol.CreateDeltaMessage(1u, fullPayload));
      }

      [Test]
      public void ManifestHandshake_ReportsLocalCompatibility()
      {
         RegisterTestTags();

         GameplayTagManifestHandshakeMessage message = GameplayTagManifestHandshakeMessage.CreateLocal();

         Assert.IsTrue(message.IsCompatibleWithLocalManifest());
      }

      private static string RegisterTestTags()
      {
         string prefix = "NetworkingTest" + System.Threading.Interlocked.Increment(ref s_TagPrefixSeed);
         GameplayTagManager.RegisterDynamicTag(prefix + ".Ability", "Ability root");
         GameplayTagManager.RegisterDynamicTag(prefix + ".Ability.Damage", "Damage branch");
         GameplayTagManager.RegisterDynamicTag(prefix + ".Ability.Damage.Fire", "Fire damage");
         GameplayTagManager.RegisterDynamicTag(prefix + ".Status", "Status root");
         GameplayTagManager.RegisterDynamicTag(prefix + ".Status.Stun", "Stun status");
         GameplayTagManager.InitializeIfNeeded();
         return prefix;
      }
   }
}
