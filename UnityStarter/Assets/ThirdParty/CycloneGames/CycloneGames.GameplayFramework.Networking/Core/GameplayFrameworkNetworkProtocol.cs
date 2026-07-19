using System;
using CycloneGames.Networking;

namespace CycloneGames.GameplayFramework.Networking
{
    public static class GameplayFrameworkNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.GameplayFramework";
        public const byte PROTOCOL_VERSION = 1;

        public const ushort MESSAGE_ID_BASE = 11000;
        public const ushort MESSAGE_ID_MAX = 11999;
        public const ushort MsgActorMigrationState = MESSAGE_ID_BASE;
        public const ushort MsgDamageRequest = MESSAGE_ID_BASE + 1;
        public const ushort MsgDamageResult = MESSAGE_ID_BASE + 2;
        public const int DamageRequestPayloadBytes = 49;
        public const int DamageResultPayloadBytes = 30;

        // Frozen FNV-1a64 identities of the versioned wire contracts ("<contract-name>:v1").
        private const ulong ACTOR_MIGRATION_STATE_SCHEMA_V1 = 0x06A6A8934573CD8EUL;
        private const ulong DAMAGE_REQUEST_SCHEMA_V1 = 0x43A411569257B773UL;
        private const ulong DAMAGE_RESULT_SCHEMA_V1 = 0x937BD1B6AA2D5D2BUL;

        private const string DAMAGE_REQUEST_WIRE_SCHEMA_V1 =
            "DamageRequestMessage:v1|Sequence:u32le@0|InstigatorActorId:i32le@4|" +
            "TargetActorId:i32le@8|WeaponOrAbilityId:i32le@12|DamageEventType:u8@16|" +
            "RequestedDamage:f32le@17|ShotOrigin:f32le[3]@21|HitLocation:f32le[3]@33|" +
            "ClientTimeSeconds:f32le@45|size:49";
        private const string DAMAGE_RESULT_WIRE_SCHEMA_V1 =
            "DamageResultMessage:v1|RequestSequence:u32le@0|InstigatorActorId:i32le@4|" +
            "TargetActorId:i32le@8|AppliedDamage:f32le@12|ResultCode:u8@16|" +
            "DamageEventType:u8@17|HitLocation:f32le[3]@18|size:30";
        private const string SERVER_DAMAGE_RESULT_CODE_WIRE_SCHEMA_V1 =
            "ServerDamageRejectReason:u8|Unknown=0|Accepted=1|InvalidPayload=2|" +
            "OwnershipMismatch=3|TargetNotDamageable=4|OutOfRange=5|OnCooldown=6|" +
            "TargetNotFound=7|Custom=8";

        public static readonly ulong DamageWireSchemaFingerprint = ComputeDamageWireSchemaFingerprint();
        public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(CreateProtocolManifest());

        public static readonly NetworkProtocolManifest DefaultManifest = Module.Manifest;
        public static readonly NetworkMessageIdRange MessageRange = Module.MessageRange;
        public static readonly ulong ProtocolFingerprint = Module.Fingerprint;

        public static bool IsGameplayFrameworkMessageId(ushort messageId)
        {
            return Module.ContainsMessageId(messageId);
        }

        public static bool IsSupportedProtocolVersion(byte protocolVersion)
        {
            return Module.IsSupportedProtocolVersion(protocolVersion);
        }

        public static bool TryRegisterMessageCatalog(INetworkMessageEndpoint messageEndpoint)
        {
            return Module.TryRegister(messageEndpoint);
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
                MESSAGE_ID_MAX)
            {
                ProtocolId = "CycloneGames.GameplayFramework.Networking",
                CurrentVersion = PROTOCOL_VERSION,
                MinimumSupportedVersion = PROTOCOL_VERSION
            };

            builder
                .SetMetadata("module", "GameplayFramework")
                .SetMetadata("damageWireSchemaFingerprint", DamageWireSchemaFingerprint.ToString("X16"))
                .AddMessage(
                    "ActorMigrationState:v1",
                    MsgActorMigrationState,
                    ACTOR_MIGRATION_STATE_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    NetworkConstants.DefaultMaxPayloadSize * 4)
                .AddMessage(
                    "DamageRequestMessage:v1",
                    MsgDamageRequest,
                    DAMAGE_REQUEST_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DamageRequestPayloadBytes)
                .AddMessage(
                    "DamageResultMessage:v1",
                    MsgDamageResult,
                    DAMAGE_RESULT_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DamageResultPayloadBytes);

            return builder.Build();
        }

        private static ulong ComputeDamageWireSchemaFingerprint()
        {
            const ulong offsetBasis = 14695981039346656037UL;
            ulong hash = AppendAscii(offsetBasis, DAMAGE_REQUEST_WIRE_SCHEMA_V1);
            hash = AppendDelimiter(hash);
            hash = AppendAscii(hash, DAMAGE_RESULT_WIRE_SCHEMA_V1);
            hash = AppendDelimiter(hash);
            hash = AppendAscii(hash, SERVER_DAMAGE_RESULT_CODE_WIRE_SCHEMA_V1);
            return hash == 0UL ? offsetBasis : hash;
        }

        private static ulong AppendAscii(ulong hash, string value)
        {
            const ulong prime = 1099511628211UL;
            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    if (character > 0x7F)
                    {
                        throw new InvalidOperationException("Wire schema descriptors must be ASCII.");
                    }

                    hash ^= (byte)character;
                    hash *= prime;
                }
            }

            return hash;
        }

        private static ulong AppendDelimiter(ulong hash)
        {
            const ulong prime = 1099511628211UL;
            unchecked
            {
                hash ^= 0xFF;
                return hash * prime;
            }
        }
    }
}
