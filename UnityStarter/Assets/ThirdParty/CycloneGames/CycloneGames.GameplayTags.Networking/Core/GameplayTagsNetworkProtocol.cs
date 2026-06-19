using System;
using CycloneGames.GameplayTags.Core;
using CycloneGames.Networking;

namespace CycloneGames.GameplayTags.Networking
{
   public enum GameplayTagNetworkPayloadKind : byte
   {
      Full = 1,
      Delta = 2
   }

   public readonly struct GameplayTagManifestHandshakeMessage
   {
      public readonly ulong ManifestHash;
      public readonly byte MinimumSupportedProtocolVersion;
      public readonly byte CurrentProtocolVersion;

      public GameplayTagManifestHandshakeMessage(
         ulong manifestHash,
         byte minimumSupportedProtocolVersion,
         byte currentProtocolVersion)
      {
         ManifestHash = manifestHash;
         MinimumSupportedProtocolVersion = minimumSupportedProtocolVersion;
         CurrentProtocolVersion = currentProtocolVersion;
      }

      public bool IsCompatibleWithLocalManifest()
      {
         return ManifestHash == GameplayTagManager.CurrentManifestHash &&
                MinimumSupportedProtocolVersion <= GameplayTagNetSerializer.CurrentProtocolVersion &&
                CurrentProtocolVersion >= GameplayTagNetSerializer.MinimumSupportedProtocolVersion;
      }

      public static GameplayTagManifestHandshakeMessage CreateLocal()
      {
         return new GameplayTagManifestHandshakeMessage(
            GameplayTagManager.CurrentManifestHash,
            GameplayTagNetSerializer.MinimumSupportedProtocolVersion,
            GameplayTagNetSerializer.CurrentProtocolVersion);
      }
   }

   public readonly struct GameplayTagPayloadMessage
   {
      public readonly uint TargetNetworkId;
      public readonly ushort Sequence;
      public readonly GameplayTagNetworkPayloadKind PayloadKind;
      public readonly byte ProtocolVersion;
      public readonly ulong ManifestHash;
      public readonly byte[] Payload;

      public GameplayTagPayloadMessage(
         uint targetNetworkId,
         ushort sequence,
         GameplayTagNetworkPayloadKind payloadKind,
         byte protocolVersion,
         ulong manifestHash,
         byte[] payload)
      {
         TargetNetworkId = targetNetworkId;
         Sequence = sequence;
         PayloadKind = payloadKind;
         ProtocolVersion = protocolVersion;
         ManifestHash = manifestHash;
         Payload = payload;
      }

      public bool IsValid
      {
         get
         {
            if (TargetNetworkId == 0u ||
                Payload == null ||
                Payload.Length == 0 ||
                !GameplayTagNetSerializer.IsSupportedProtocolVersion(ProtocolVersion))
            {
               return false;
            }

            if (!GameplayTagNetSerializer.TryGetPacketKind(Payload, 0, out GameplayTagNetPacketKind packetKind))
               return false;

            return (PayloadKind == GameplayTagNetworkPayloadKind.Full && packetKind == GameplayTagNetPacketKind.Full) ||
                   (PayloadKind == GameplayTagNetworkPayloadKind.Delta && packetKind == GameplayTagNetPacketKind.Delta);
         }
      }
   }

   public readonly struct GameplayTagFullStateRequestMessage
   {
      public readonly uint TargetNetworkId;
      public readonly ulong ManifestHash;

      public GameplayTagFullStateRequestMessage(uint targetNetworkId, ulong manifestHash)
      {
         TargetNetworkId = targetNetworkId;
         ManifestHash = manifestHash;
      }

      public static GameplayTagFullStateRequestMessage CreateLocal(uint targetNetworkId)
      {
         return new GameplayTagFullStateRequestMessage(targetNetworkId, GameplayTagManager.CurrentManifestHash);
      }
   }

   public static class GameplayTagsNetworkProtocol
   {
      public const string MessageOwner = "CycloneGames.GameplayTags";

      public const ushort MESSAGE_ID_BASE = 12000;
      public const ushort MESSAGE_ID_MAX = 12999;
      public const ushort MsgManifestHandshake = MESSAGE_ID_BASE;
      public const ushort MsgFullState = MESSAGE_ID_BASE + 1;
      public const ushort MsgDelta = MESSAGE_ID_BASE + 2;
      public const ushort MsgFullStateRequest = MESSAGE_ID_BASE + 3;

      public const int DefaultMaxFullStatePayloadSize = NetworkConstants.DefaultMaxPayloadSize * 8;
      public const int DefaultMaxDeltaPayloadSize = NetworkConstants.DefaultMaxPayloadSize * 2;

      public static readonly NetworkMessageIdRange MessageRange = new NetworkMessageIdRange(
         MessageOwner,
         MESSAGE_ID_BASE,
         MESSAGE_ID_MAX,
         NetworkMessageKind.Module);

      public static bool IsGameplayTagsMessageId(ushort messageId)
      {
         return MessageRange.Contains(messageId);
      }

      public static bool TryRegisterMessageCatalog(INetworkManager networkManager)
      {
         if (networkManager == null)
            return false;

         if (networkManager is not INetworkRuntimeContextProvider provider || provider.RuntimeContext == null)
            return false;

         if (!provider.RuntimeContext.TryGetService(out INetworkMessageCatalog catalog))
            return false;

         RegisterMessageCatalog(catalog);
         return true;
      }

      public static void RegisterMessageCatalog(INetworkMessageCatalog catalog)
      {
         if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));

         catalog.RegisterModuleRange(MessageRange);

         RegisterMessage<GameplayTagManifestHandshakeMessage>(
            catalog,
            MsgManifestHandshake,
            NetworkChannel.Reliable,
            32);

         RegisterMessage<GameplayTagPayloadMessage>(
            catalog,
            MsgFullState,
            NetworkChannel.Reliable,
            DefaultMaxFullStatePayloadSize);

         RegisterMessage<GameplayTagPayloadMessage>(
            catalog,
            MsgDelta,
            NetworkChannel.Reliable,
            DefaultMaxDeltaPayloadSize);

         RegisterMessage<GameplayTagFullStateRequestMessage>(
            catalog,
            MsgFullStateRequest,
            NetworkChannel.Reliable,
            32);
      }

      public static void RegisterMessage<T>(
         INetworkMessageCatalog catalog,
         ushort messageId,
         NetworkChannel channel = NetworkChannel.Reliable,
         int maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize) where T : struct
      {
         if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));

         if (!IsGameplayTagsMessageId(messageId))
         {
            throw new ArgumentOutOfRangeException(
               nameof(messageId),
               messageId,
               $"GameplayTags message ids must be inside {MessageRange}.");
         }

         NetworkMessageDescriptor descriptor = NetworkMessageDescriptor.Create<T>(
            messageId,
            MessageOwner,
            NetworkMessageKind.Module,
            channel,
            maxPayloadSize);

         if (catalog.TryRegister(descriptor))
            return;

         if (catalog.TryGet(messageId, out NetworkMessageDescriptor existing)
             && existing.SchemaHash == descriptor.SchemaHash
             && string.Equals(existing.Owner, descriptor.Owner, StringComparison.Ordinal)
             && string.Equals(existing.Name, descriptor.Name, StringComparison.Ordinal)
             && existing.Kind == descriptor.Kind
             && existing.DefaultChannel == descriptor.DefaultChannel
             && existing.MaxPayloadSize == descriptor.MaxPayloadSize)
         {
            return;
         }

         throw new InvalidOperationException($"Message id {messageId} is already registered by {existing.Owner}:{existing.Name}.");
      }

      public static GameplayTagPayloadMessage CreateFullStateMessage(
         uint targetNetworkId,
         byte[] payload,
         ushort sequence = 0)
      {
         return CreatePayloadMessage(
            targetNetworkId,
            sequence,
            GameplayTagNetworkPayloadKind.Full,
            GameplayTagNetPacketKind.Full,
            payload);
      }

      public static GameplayTagPayloadMessage CreateDeltaMessage(
         uint targetNetworkId,
         byte[] payload,
         ushort sequence = 0)
      {
         return CreatePayloadMessage(
            targetNetworkId,
            sequence,
            GameplayTagNetworkPayloadKind.Delta,
            GameplayTagNetPacketKind.Delta,
            payload);
      }

      private static GameplayTagPayloadMessage CreatePayloadMessage(
         uint targetNetworkId,
         ushort sequence,
         GameplayTagNetworkPayloadKind payloadKind,
         GameplayTagNetPacketKind expectedPacketKind,
         byte[] payload)
      {
         if (targetNetworkId == 0u)
            throw new ArgumentOutOfRangeException(nameof(targetNetworkId));

         if (!GameplayTagNetSerializer.TryGetPacketKind(payload, 0, out GameplayTagNetPacketKind packetKind) ||
             packetKind != expectedPacketKind)
         {
            throw new ArgumentException("Gameplay tag payload kind does not match the requested network message.", nameof(payload));
         }

         return new GameplayTagPayloadMessage(
            targetNetworkId,
            sequence,
            payloadKind,
            payload[0],
            GameplayTagManager.CurrentManifestHash,
            payload);
      }
   }
}
