using System;

using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public sealed class DefaultMovementNetworkInputValidator : IMovementNetworkInputValidator
    {
        private const float NORMALIZED_AIM_TOLERANCE = 0.20f;

        public static readonly DefaultMovementNetworkInputValidator Instance = new DefaultMovementNetworkInputValidator();

        private DefaultMovementNetworkInputValidator()
        {
        }

        public NetworkActionResult Validate(
            in MovementInputCommandMessage command,
            in MovementNetworkInputValidationContext context)
        {
            if (!command.IsValid)
            {
                return Reject(NetworkActionResultCode.InvalidPayload, command, context.ServerTick);
            }

            if (!context.AllowsButtonMask(command.ButtonMask) || !context.AllowsCustomFlags(command.CustomFlags))
            {
                return Reject(NetworkActionResultCode.InvalidPayload, command, context.ServerTick);
            }

            if (command.MoveAxes.SqrMagnitude > context.MaxMoveAxesMagnitudeSqr)
            {
                return Reject(NetworkActionResultCode.InvalidPayload, command, context.ServerTick);
            }

            float aimMagnitudeSqr = command.AimDirection.SqrMagnitude;
            if (aimMagnitudeSqr < context.MinAimDirectionMagnitudeSqr
                || aimMagnitudeSqr > context.MaxAimDirectionMagnitudeSqr)
            {
                return Reject(NetworkActionResultCode.InvalidPayload, command, context.ServerTick);
            }

            if (context.RequireNormalizedAimDirection && aimMagnitudeSqr > 0f)
            {
                float aimMagnitude = MathF.Sqrt(aimMagnitudeSqr);
                if (MathF.Abs(aimMagnitude - 1f) > NORMALIZED_AIM_TOLERANCE)
                {
                    return Reject(NetworkActionResultCode.InvalidPayload, command, context.ServerTick);
                }
            }

            NetworkActionCommand actionCommand = command.ToNetworkActionCommand();
            NetworkActionValidationContext actionContext = context.ToActionValidationContext();
            return DefaultNetworkActionValidator.Instance.Validate(actionCommand, actionContext);
        }

        private static NetworkActionResult Reject(
            NetworkActionResultCode code,
            in MovementInputCommandMessage command,
            NetworkTickId serverTick)
        {
            return NetworkActionResult.Reject(
                code,
                serverTick,
                command.InputSequence,
                command.PredictionKey);
        }
    }
}
