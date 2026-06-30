using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public sealed class DefaultProjectileNetworkMessageValidator : IProjectileNetworkMessageValidator
    {
        private const float DIRECTION_EPSILON = 0.000001f;

        public static readonly DefaultProjectileNetworkMessageValidator Instance = new DefaultProjectileNetworkMessageValidator();

        private DefaultProjectileNetworkMessageValidator()
        {
        }

        public NetworkActionResult ValidateSpawn(
            in ProjectileSpawnMessage message,
            in ProjectileNetworkValidationContext context)
        {
            if (!message.IsValid
                || message.Direction.SqrMagnitude <= DIRECTION_EPSILON
                || message.InitialVelocity.SqrMagnitude > context.MaxInitialVelocitySqr
                || !context.AllowsLifecycleFlags(message.LifecycleFlags))
            {
                return Reject(NetworkActionResultCode.InvalidPayload, message.ServerTick, 0, message.PredictionKey);
            }

            return ValidateEnvelope(message.ServerTick, 0, message.PredictionKey, context);
        }

        public NetworkActionResult ValidateSnapshot(
            in ProjectileSnapshotMessage message,
            in ProjectileNetworkValidationContext context)
        {
            if (!message.IsValid
                || message.Velocity.SqrMagnitude > context.MaxSnapshotVelocitySqr
                || message.Radius > context.MaxRadius
                || message.Age > context.MaxAge
                || !context.AllowsLifecycleFlags(message.LifecycleFlags))
            {
                return Reject(NetworkActionResultCode.InvalidPayload, message.ServerTick, message.Sequence, message.PredictionKey);
            }

            return ValidateEnvelope(message.ServerTick, message.Sequence, message.PredictionKey, context);
        }

        public NetworkActionResult ValidateHit(
            in ProjectileHitMessage message,
            in ProjectileNetworkValidationContext context)
        {
            if (!message.IsValid)
            {
                return Reject(NetworkActionResultCode.InvalidPayload, message.ServerTick, 0, message.PredictionKey);
            }

            return ValidateEnvelope(message.ServerTick, 0, message.PredictionKey, context);
        }

        public NetworkActionResult ValidateDespawn(
            in ProjectileDespawnMessage message,
            in ProjectileNetworkValidationContext context)
        {
            if (!message.IsValid)
            {
                return Reject(NetworkActionResultCode.InvalidPayload, message.ServerTick, message.Sequence);
            }

            return ValidateEnvelope(message.ServerTick, message.Sequence, 0, context);
        }

        public NetworkActionResult ValidateFullStateRequest(
            in ProjectileFullStateRequestMessage message,
            in ProjectileNetworkValidationContext context)
        {
            if (!message.IsValid)
            {
                return Reject(NetworkActionResultCode.InvalidPayload, message.ClientTick, message.Sequence);
            }

            if (!context.IsAuthenticated)
            {
                return Reject(NetworkActionResultCode.Unauthorized, message.ClientTick, message.Sequence);
            }

            return NetworkActionResult.Accept(new NetworkTickId(message.ClientTick), message.Sequence);
        }

        private static NetworkActionResult ValidateEnvelope(
            int serverTick,
            ushort sequence,
            int predictionKey,
            in ProjectileNetworkValidationContext context)
        {
            var tick = new NetworkTickId(serverTick);
            if (!context.IsAuthenticated)
            {
                return NetworkActionResult.Reject(
                    NetworkActionResultCode.Unauthorized,
                    context.ServerTick,
                    sequence,
                    predictionKey);
            }

            if (!context.IsTickInAcceptedWindow(tick))
            {
                return NetworkActionResult.Reject(
                    NetworkActionResultCode.Expired,
                    context.ServerTick,
                    sequence,
                    predictionKey);
            }

            if (context.IsDuplicate(tick, sequence))
            {
                return NetworkActionResult.Reject(
                    NetworkActionResultCode.Duplicate,
                    context.ServerTick,
                    sequence,
                    predictionKey);
            }

            if (!context.IsSequenceOrdered(tick, sequence))
            {
                return NetworkActionResult.Reject(
                    NetworkActionResultCode.OutOfOrder,
                    context.ServerTick,
                    sequence,
                    predictionKey);
            }

            return NetworkActionResult.Accept(tick, sequence, predictionKey);
        }

        private static NetworkActionResult Reject(
            NetworkActionResultCode code,
            int tick,
            ushort sequence,
            int predictionKey = 0)
        {
            return NetworkActionResult.Reject(
                code,
                new NetworkTickId(tick),
                sequence,
                predictionKey);
        }
    }
}
