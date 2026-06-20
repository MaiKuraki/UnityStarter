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

   public readonly struct GameplayTagManifestHandshakeMessage : INetworkProtocolHandshakeMessage
   {
      public readonly ulong ProtocolFingerprint;
      public readonly ulong ManifestHash;
      public readonly byte MinimumSupportedProtocolVersion;
      public readonly byte CurrentProtocolVersion;

      public GameplayTagManifestHandshakeMessage(
         ulong protocolFingerprint,
         ulong manifestHash,
         byte minimumSupportedProtocolVersion,
         byte currentProtocolVersion)
      {
         ProtocolFingerprint = protocolFingerprint;
         ManifestHash = manifestHash;
         MinimumSupportedProtocolVersion = minimumSupportedProtocolVersion;
         CurrentProtocolVersion = currentProtocolVersion;
      }

      ulong INetworkProtocolHandshakeMessage.ProtocolFingerprint => ProtocolFingerprint;
      byte INetworkProtocolHandshakeMessage.CurrentProtocolVersion => CurrentProtocolVersion;
      byte INetworkProtocolHandshakeMessage.MinimumSupportedProtocolVersion => MinimumSupportedProtocolVersion;
      ulong INetworkProtocolHandshakeMessage.DomainStateHash => ManifestHash;

      public bool IsCompatibleWithLocalManifest()
      {
         return NetworkProtocolHandshake.IsCompatible(
            this,
            GameplayTagsNetworkProtocol.Module,
            GameplayTagManager.CurrentManifestHash,
            requireDomainStateMatch: true);
      }

      public static GameplayTagManifestHandshakeMessage CreateLocal()
      {
         return new GameplayTagManifestHandshakeMessage(
            GameplayTagsNetworkProtocol.ProtocolFingerprint,
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

      public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(CreateProtocolManifest());

      public static readonly NetworkProtocolManifest DefaultManifest = Module.Manifest;
      public static readonly NetworkMessageIdRange MessageRange = Module.MessageRange;
      public static readonly ulong ProtocolFingerprint = Module.Fingerprint;

      public static bool IsGameplayTagsMessageId(ushort messageId)
      {
         return Module.ContainsMessageId(messageId);
      }

      public static bool IsSupportedProtocolVersion(byte protocolVersion)
      {
         return Module.IsSupportedProtocolVersion(protocolVersion);
      }

      public static bool TryRegisterMessageCatalog(INetworkManager networkManager)
      {
         return Module.TryRegister(networkManager);
      }

      public static void RegisterMessageCatalog(INetworkMessageCatalog catalog)
      {
         Module.Register(catalog);
      }

      public static NetworkProtocolManifest CreateProtocolManifest()
      {
         var builder = new NetworkProtocolManifestBuilder(
            MessageOwner,
            MESSAGE_ID_BASE,
            MESSAGE_ID_MAX,
            NetworkMessageKind.Module)
         {
            ProtocolId = "CycloneGames.GameplayTags.Networking",
            CurrentVersion = GameplayTagNetSerializer.CurrentProtocolVersion,
            MinimumSupportedVersion = GameplayTagNetSerializer.MinimumSupportedProtocolVersion
         };

         builder
            .SetMetadata("module", "GameplayTags")
            .AddMessage<GameplayTagManifestHandshakeMessage>(MsgManifestHandshake, NetworkChannel.Reliable, 32)
            .AddMessage<GameplayTagPayloadMessage>(MsgFullState, NetworkChannel.Reliable, DefaultMaxFullStatePayloadSize)
            .AddMessage<GameplayTagPayloadMessage>(MsgDelta, NetworkChannel.Reliable, DefaultMaxDeltaPayloadSize)
            .AddMessage<GameplayTagFullStateRequestMessage>(MsgFullStateRequest, NetworkChannel.Reliable, 32);

         return builder.Build();
      }

      public static void RegisterMessage<T>(
         INetworkMessageCatalog catalog,
         ushort messageId,
         NetworkChannel channel = NetworkChannel.Reliable,
         int maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize) where T : struct
      {
         Module.RegisterMessage<T>(catalog, messageId, channel, maxPayloadSize);
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
