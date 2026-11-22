using System;
using CycloneGames.Networking;
using CycloneGames.Networking.Security;

namespace CycloneGames.GameplayFramework.Networking
{
    public static class GameplayFrameworkNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.GameplayFramework";

        public const ushort MESSAGE_ID_BASE = 11000;
        public const ushort MESSAGE_ID_MAX = 11999;
        public const ushort MsgActorMigrationState = MESSAGE_ID_BASE;
        public const ushort MsgDamageRequest = MESSAGE_ID_BASE + 1;
        public const ushort MsgDamageResult = MESSAGE_ID_BASE + 2;
        public const int DamageRequestPayloadBytes = 49;
        public const int DamageResultPayloadBytes = 30;

        // Frozen FNV-1a64 identities of the wire contracts.
        private const ulong ACTOR_MIGRATION_STATE_SCHEMA = 0xAE2721975C24EE7FUL;
        private const ulong DAMAGE_REQUEST_SCHEMA = 0x0B5E6553736B4FF8UL;
        private const ulong DAMAGE_RESULT_SCHEMA = 0x735B5BC2DB7407B0UL;

        private const string DAMAGE_REQUEST_WIRE_SCHEMA =
            "DamageRequestMessage|Sequence:u32le@0|InstigatorActorId:i32le@4|" +
            "TargetActorId:i32le@8|WeaponOrAbilityId:i32le@12|DamageEventType:u8@16|" +
            "RequestedDamage:f32le@17|ShotOrigin:f32le[3]@21|HitLocation:f32le[3]@33|" +
            "ClientTimeSeconds:f32le@45|size:49";
        private const string DAMAGE_RESULT_WIRE_SCHEMA =
            "DamageResultMessage|RequestSequence:u32le@0|InstigatorActorId:i32le@4|" +
            "TargetActorId:i32le@8|AppliedDamage:f32le@12|ResultCode:u8@16|" +
            "DamageEventType:u8@17|HitLocation:f32le[3]@18|size:30";
        private const string SERVER_DAMAGE_RESULT_CODE_WIRE_SCHEMA =
            "ServerDamageRejectReason:u8|Unknown=0|Accepted=1|InvalidPayload=2|" +
            "OwnershipMismatch=3|TargetNotDamageable=4|OutOfRange=5|OnCooldown=6|" +
            "TargetNotFound=7|Custom=8|CooldownCapacityReached=9";

        public static readonly ulong DamageWireSchemaFingerprint = ComputeDamageWireSchemaFingerprint();
        public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(CreateProtocolManifest());

        public static readonly NetworkProtocolManifest DefaultManifest = Module.Manifest;
        public static readonly NetworkMessageIdRange MessageRange = Module.MessageRange;
        public static readonly ulong ProtocolFingerprint = Module.Fingerprint;

        public static bool IsGameplayFrameworkMessageId(ushort messageId)
        {
            return Module.ContainsMessageId(messageId);
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
            };

            builder
                .SetMetadata("module", "GameplayFramework")
                .SetMetadata("damageWireSchemaFingerprint", DamageWireSchemaFingerprint.ToString("X16"))
                .AddMessage(
                    "ActorMigrationState",
                    MsgActorMigrationState,
                    ACTOR_MIGRATION_STATE_SCHEMA,
                    NetworkChannel.Reliable,
                    ActorMigrationNetworkingExtensions.MaximumEncodedSize)
                .AddMessage(
                    "DamageRequestMessage",
                    MsgDamageRequest,
                    DAMAGE_REQUEST_SCHEMA,
                    NetworkChannel.Reliable,
                    DamageRequestPayloadBytes)
                .AddMessage(
                    "DamageResultMessage",
                    MsgDamageResult,
                    DAMAGE_RESULT_SCHEMA,
                    NetworkChannel.Reliable,
                    DamageResultPayloadBytes);

            return builder.Build();
        }

        private static ulong ComputeDamageWireSchemaFingerprint()
        {
            const ulong offsetBasis = 14695981039346656037UL;
            ulong hash = AppendAscii(offsetBasis, DAMAGE_REQUEST_WIRE_SCHEMA);
            hash = AppendDelimiter(hash);
            hash = AppendAscii(hash, DAMAGE_RESULT_WIRE_SCHEMA);
            hash = AppendDelimiter(hash);
            hash = AppendAscii(hash, SERVER_DAMAGE_RESULT_CODE_WIRE_SCHEMA);
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

    /// <summary>
    /// Installs fail-closed policies for every GameplayFramework wire contract. Migration direction is
    /// deployment-specific and therefore must be supplied explicitly by the composition root.
    /// </summary>
    public static class GameplayFrameworkNetworkSecurityPolicies
    {
        private const NetworkMessageDirectionMask DamageResultDirections =
            NetworkMessageDirectionMask.ServerToClient | NetworkMessageDirectionMask.ServerBroadcast;

        public static void Configure(
            INetworkSecurityPolicyConfigurable configurable,
            NetworkMessageDirectionMask migrationDirections,
            bool requireEncryptedTransport = false,
            bool requireSignature = false)
        {
            if (configurable == null)
            {
                throw new ArgumentNullException(nameof(configurable));
            }

            if (migrationDirections == NetworkMessageDirectionMask.None
                || (migrationDirections & ~NetworkMessageDirectionMask.Any) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(migrationDirections));
            }

            configurable.SetMessageSecurityPolicy(
                GameplayFrameworkNetworkProtocol.MsgActorMigrationState,
                CreatePolicy(
                    migrationDirections,
                    ActorMigrationNetworkingExtensions.MaximumEncodedSize,
                    requireEncryptedTransport,
                    requireSignature));
            configurable.SetMessageSecurityPolicy(
                GameplayFrameworkNetworkProtocol.MsgDamageRequest,
                CreatePolicy(
                    NetworkMessageDirectionMask.ClientToServer,
                    GameplayFrameworkNetworkProtocol.DamageRequestPayloadBytes,
                    requireEncryptedTransport,
                    requireSignature));
            configurable.SetMessageSecurityPolicy(
                GameplayFrameworkNetworkProtocol.MsgDamageResult,
                CreatePolicy(
                    DamageResultDirections,
                    GameplayFrameworkNetworkProtocol.DamageResultPayloadBytes,
                    requireEncryptedTransport,
                    requireSignature));
        }

        private static MessageSecurityPolicy CreatePolicy(
            NetworkMessageDirectionMask directions,
            int maxPayloadSize,
            bool requireEncryptedTransport,
            bool requireSignature)
        {
            return new MessageSecurityPolicy(
                directions,
                maxPayloadSize,
                requireAuthenticatedConnection: true,
                requireEncryptedTransport: requireEncryptedTransport,
                enableReplayProtection: true,
                requireSignature: requireSignature);
        }
    }
}
