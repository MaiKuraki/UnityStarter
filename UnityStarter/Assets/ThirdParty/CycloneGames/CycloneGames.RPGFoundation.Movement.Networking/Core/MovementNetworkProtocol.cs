using System;
using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public enum MovementWireLayoutType
    {
        Handshake,
        InputCommand,
        Snapshot,
        FullStateMessage,
        FullState,
        DeltaSnapshot,
        Correction,
        FullStateRequest,
        AuthorityTransfer,
        Teleport,
        InputBatch,
        InputAck
    }

    public static class MovementWireLayout
    {
        private const ulong OFFSET_BASIS = 14695981039346656037UL;
        private const ulong PRIME = 1099511628211UL;

        private static readonly string[] DESCRIPTORS =
        {
            "ProtocolFingerprint:u64,WireLayoutDigest:u64,WireCodecDigest:u64,DomainStateHash:u64,MessageIdMin:u16,MessageIdMax:u16,FeatureMask:u16",
            "EntityId:u64,Epoch:u64,ClientTick:i64,LastReceivedServerTick:i64,InputSequence:u16,PredictionKey:i32,ButtonMask:u32,CustomFlags:u32,DeltaTime:f32,MoveAxes:vec3,AimDirection:vec3",
            "EntityId:u64,Epoch:u64,Tick:i64,ServerTick:i64,Sequence:u16,StateId:u16,Flags:u8,JumpCount:u8,VerticalVelocity:f32,NetworkTimeSeconds:f64,Position:vec3,Velocity:vec3,WorldUp:vec3",
            "EntityId:u64,Epoch:u64,ServerTick:i64,Sequence:u16,State:fullState",
            "NetworkTick:i64,NetworkTimeSeconds:f64,Position:mv3,Velocity:mv3,WorldUp:mv3,GroundNormal:mv3,InputDirection:mv3,FacingDirection:mv3,RollDirection:mv3,WallJumpDirection:mv3,LastWallNormal:mv3,WallClimbNormal:mv3,PendingImpulse:mv3,PendingForce:mv3,PlatformLocalPosition:mv3,PlatformVelocity:mv3,PlatformLocalRotationX:f32,PlatformLocalRotationY:f32,PlatformLocalRotationZ:f32,PlatformLocalRotationW:f32,VerticalVelocity:f32,SlopeAngle:f32,RollTimer:f32,WallClingTimer:f32,LastWallJumpTime:f32,StateElapsed:f32,UnconstrainedTimer:f32,CurrentSpeed:f32,JumpCount:i32,StateTick:i32,Extension0:f32,Extension1:f32,Extension2:f32,Extension3:f32,ExtensionInt0:i32,ExtensionInt1:i32,StateId:u16,ClimbingMode:u8,IsGrounded:bool,WasGrounded:bool,IsOnNonWalkableSlope:bool,IsWallJumping:bool,IsWallSliding:bool,IsOnPlatform:bool,UseRootMotion:bool,JumpPressed:bool,SprintHeld:bool,CrouchHeld:bool,RollPressed:bool",
            "Position:mv3,Velocity:mv3,WorldUp:mv3,StateId:u16,Flags:u8,JumpCount:u8,VerticalVelocity:f32,IsGrounded:bool,NetworkTick:i64,NetworkTimeSeconds:f64",
            "EntityId:u64,Epoch:u64,CorrectedClientTick:i64,ServerTick:i64,InputSequence:u16,PositionErrorSqr:f32,FullState:fullStateMessage",
            "EntityId:u64,Epoch:u64,RequesterConnectionId:i32,RequesterPlayerId:u64,LastKnownTick:i64,RequestSequence:u16,ReasonCode:u32",
            "EntityId:u64,Epoch:u64,ProtocolFingerprint:u64,TransferSequence:u32,ServerTick:i64,PreviousAuthorityConnectionId:i32,NextAuthorityConnectionId:i32,PreviousAuthorityPlayerId:u64,NextAuthorityPlayerId:u64,ReasonCode:u32",
            "EntityId:u64,Epoch:u64,ServerTick:i64,TeleportSequence:u16,StateId:u16,Flags:u8,ReasonCode:u32,Position:vec3,Velocity:vec3,WorldUp:vec3",
            "EntityId:u64,Epoch:u64,Count:u8,Command0:inputCommand,Command1:inputCommand,Command2:inputCommand,Command3:inputCommand",
            "EntityId:u64,Epoch:u64,ServerTick:i64,AckedClientTick:i64,AckedInputSequence:u16,GoodMove:bool"
        };

        public static readonly ulong Digest = ComputeDigest();

        public static int DescriptorCount
        {
            get { return DESCRIPTORS.Length; }
        }

        public static string GetDescriptor(MovementWireLayoutType type)
        {
            int index = (int)type;
            if (index < 0 || index >= DESCRIPTORS.Length) return string.Empty;
            return DESCRIPTORS[index];
        }

        private static ulong ComputeDigest()
        {
            ulong hash = OFFSET_BASIS;
            unchecked
            {
                for (int i = 0; i < DESCRIPTORS.Length; i++)
                {
                    string descriptor = DESCRIPTORS[i];
                    for (int j = 0; j < descriptor.Length; j++)
                    {
                        hash ^= (byte)descriptor[j];
                        hash *= PRIME;
                    }
                    hash ^= (byte)(i + 1);
                    hash *= PRIME;
                }
            }
            return hash == 0UL ? 1UL : hash;
        }
    }

    public readonly struct MovementWireCodecPolicy
    {
        public readonly string SerializerId;
        public readonly byte CompressionMode;
        public readonly byte PositionQuantization;
        public readonly byte VelocityQuantization;
        public readonly byte RotationQuantization;
        public readonly bool LittleEndian;

        public MovementWireCodecPolicy(
            string serializerId,
            byte compressionMode = 0,
            byte positionQuantization = 0,
            byte velocityQuantization = 0,
            byte rotationQuantization = 0,
            bool littleEndian = true)
        {
            if (string.IsNullOrEmpty(serializerId))
                throw new ArgumentException("A stable printable serializer identity is required.", nameof(serializerId));
            for (int i = 0; i < serializerId.Length; i++)
            {
                char c = serializerId[i];
                if (c < '!' || c > '~')
                    throw new ArgumentException("Serializer identity must be printable ASCII without spaces.", nameof(serializerId));
            }
            SerializerId = serializerId;
            CompressionMode = compressionMode;
            PositionQuantization = positionQuantization;
            VelocityQuantization = velocityQuantization;
            RotationQuantization = rotationQuantization;
            LittleEndian = littleEndian;
        }

        public static MovementWireCodecPolicy Unspecified
        {
            get { return new MovementWireCodecPolicy("CycloneGames.Movement.NoCodec"); }
        }

        public ulong ComputeDigest()
        {
            ulong hash = 14695981039346656037UL;
            unchecked
            {
                for (int i = 0; i < SerializerId.Length; i++)
                {
                    hash ^= (byte)SerializerId[i];
                    hash *= 1099511628211UL;
                }
                hash ^= CompressionMode; hash *= 1099511628211UL;
                hash ^= PositionQuantization; hash *= 1099511628211UL;
                hash ^= VelocityQuantization; hash *= 1099511628211UL;
                hash ^= RotationQuantization; hash *= 1099511628211UL;
                hash ^= LittleEndian ? (byte)1 : (byte)0; hash *= 1099511628211UL;
            }
            return hash == 0UL ? 1UL : hash;
        }
    }

    // The protocol is owned by the composition root. Contract identities are
    // checked-in strings, not CLR type names.
    public sealed class MovementNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.RPGFoundation.Movement";
        private const string PROTOCOL_ID = "com.cyclone-games.rpg-foundation-movement-networking";

        private static readonly string[] CONTRACT_IDS =
        {
            "CycloneGames.Movement.ManifestHandshake",
            "CycloneGames.Movement.InputCommand",
            "CycloneGames.Movement.AuthoritativeSnapshot",
            "CycloneGames.Movement.Correction",
            "CycloneGames.Movement.FullStateRequest",
            "CycloneGames.Movement.AuthorityTransfer",
            "CycloneGames.Movement.Teleport",
            "CycloneGames.Movement.InputBatch",
            "CycloneGames.Movement.InputAck"
        };

        private static readonly ulong[] SCHEMA_HASHES =
        {
            0x8DED5E24D7B39620UL,
            0xED78ABB07643CD0BUL,
            0xC1C6113BDC3A3C75UL,
            0x3A6784AD7C5B4A62UL,
            0x725878DDCFCFC2C1UL,
            0x46743147D1ACD32CUL,
            0x2806F1DDC9C488E9UL,
            0x8F143D2B50EC0BA6UL,
            0xAA1B6FB2703C77F7UL
        };

        private readonly NetworkModuleProtocol _module;
        private readonly ulong _wireCodecDigest;
        private readonly MovementWireCodecPolicy _codecPolicy;

        public MovementNetworkProtocol(MovementWireCodecPolicy codecPolicy)
        {
            _codecPolicy = codecPolicy;
            _wireCodecDigest = codecPolicy.ComputeDigest();
            _module = new NetworkModuleProtocol(CreateProtocolManifest());
        }

        public NetworkModuleProtocol Module { get { return _module; } }
        public NetworkProtocolManifest Manifest { get { return _module.Manifest; } }
        public NetworkMessageIdRange MessageRange { get { return _module.MessageRange; } }
        public ulong ProtocolFingerprint { get { return _module.Fingerprint; } }
        public ulong WireLayoutDigest { get { return MovementWireLayout.Digest; } }
        public ulong WireCodecDigest { get { return _wireCodecDigest; } }
        public MovementWireCodecPolicy CodecPolicy { get { return _codecPolicy; } }

        public bool IsMovementMessageId(ushort messageId)
        {
            return MessageRange.Contains(messageId);
        }

        public bool IsBuiltInMovementMessage(ushort messageId)
        {
            return MovementWireIds.IsBuiltInMessage(messageId);
        }

        public bool TryRegisterMessageCatalog(INetworkMessageEndpoint messageEndpoint)
        {
            return _module.TryRegister(messageEndpoint);
        }

        public void RegisterMessageCatalog(INetworkMessageCatalog catalog)
        {
            _module.Register(catalog);
        }

        public int GetEffectiveMaxPayloadSize(ushort messageId, INetworkMessageEndpoint endpoint)
        {
            for (int i = 0; i < _module.Manifest.Messages.Count; i++)
            {
                NetworkMessageDescriptor descriptor = _module.Manifest.Messages[i];
                if (descriptor.MessageId != messageId) continue;
                int declared = descriptor.MaxPayloadSize;
                if (endpoint == null) return declared;
                int endpointBudget = endpoint.GetMaxPayloadSize(messageId, descriptor.DefaultChannel);
                return endpointBudget > 0 && endpointBudget < declared ? endpointBudget : declared;
            }
            return -1;
        }

        public MovementManifestHandshakeMessage CreateHandshake(ulong domainStateHash = 0UL)
        {
            return new MovementManifestHandshakeMessage(
                ProtocolFingerprint, WireLayoutDigest, WireCodecDigest, domainStateHash,
                MovementWireIds.MessageIdBase, MovementWireIds.MessageIdMax, 0);
        }

        public static string GetContractId(int index)
        {
            if (index < 0 || index >= CONTRACT_IDS.Length) return string.Empty;
            return CONTRACT_IDS[index];
        }

        public static int ContractCount { get { return CONTRACT_IDS.Length; } }

        public static NetworkProtocolManifest CreateProtocolManifest()
        {
            var builder = new NetworkProtocolManifestBuilder(
                MessageOwner,
                MovementWireIds.MessageIdBase,
                MovementWireIds.MessageIdMax)
            {
                ProtocolId = PROTOCOL_ID
            };

            builder
                .SetMetadata("module", "RPGFoundation.Movement")
                .SetMetadata("authority", "Epoch-bound server authority")
                .SetMetadata("layout", "Wire layout and codec digests validated during handshake")
                .AddMessage(CONTRACT_IDS[0], MovementWireIds.ManifestHandshake, SCHEMA_HASHES[0],
                    NetworkChannel.Reliable, MovementWireLimits.MaxControlPayloadSize)
                .AddMessage(CONTRACT_IDS[1], MovementWireIds.InputCommand, SCHEMA_HASHES[1],
                    NetworkChannel.Unreliable, MovementWireLimits.MaxInputPayloadSize)
                .AddMessage(CONTRACT_IDS[2], MovementWireIds.AuthoritativeSnapshot, SCHEMA_HASHES[2],
                    NetworkChannel.UnreliableSequenced, MovementWireLimits.MaxSnapshotPayloadSize)
                .AddMessage(CONTRACT_IDS[3], MovementWireIds.Correction, SCHEMA_HASHES[3],
                    NetworkChannel.Reliable, MovementWireLimits.MaxCorrectionPayloadSize)
                .AddMessage(CONTRACT_IDS[4], MovementWireIds.FullStateRequest, SCHEMA_HASHES[4],
                    NetworkChannel.Reliable, MovementWireLimits.MaxControlPayloadSize)
                .AddMessage(CONTRACT_IDS[5], MovementWireIds.AuthorityTransfer, SCHEMA_HASHES[5],
                    NetworkChannel.Reliable, MovementWireLimits.MaxControlPayloadSize)
                .AddMessage(CONTRACT_IDS[6], MovementWireIds.Teleport, SCHEMA_HASHES[6],
                    NetworkChannel.Reliable, MovementWireLimits.MaxSnapshotPayloadSize)
                .AddMessage(CONTRACT_IDS[7], MovementWireIds.InputBatch, SCHEMA_HASHES[7],
                    NetworkChannel.Unreliable, MovementWireLimits.MaxInputBatchPayloadSize)
                .AddMessage(CONTRACT_IDS[8], MovementWireIds.InputAck, SCHEMA_HASHES[8],
                    NetworkChannel.Unreliable, MovementWireLimits.MaxInputAckPayloadSize);

            return builder.Build();
        }
    }
}
