namespace CycloneGames.Networking.Simulation
{
    public sealed class DefaultNetworkActionValidator : INetworkActionValidator
    {
        public static readonly DefaultNetworkActionValidator Instance = new DefaultNetworkActionValidator();

        private DefaultNetworkActionValidator()
        {
        }

        public NetworkActionResult Validate(
            in NetworkActionCommand command,
            in NetworkActionValidationContext context)
        {
            if (!command.IsValid)
            {
                return NetworkActionResult.Reject(
                    NetworkActionResultCode.InvalidPayload,
                    context.ServerTick,
                    command.Sequence,
                    command.PredictionKey);
            }

            if (!context.IsAuthenticated)
            {
                return NetworkActionResult.Reject(
                    NetworkActionResultCode.Unauthorized,
                    context.ServerTick,
                    command.Sequence,
                    command.PredictionKey);
            }

            if (context.IsDuplicate(command.ClientTick, command.Sequence))
            {
                return NetworkActionResult.Reject(
                    NetworkActionResultCode.Duplicate,
                    context.ServerTick,
                    command.Sequence,
                    command.PredictionKey);
            }

            if (!context.IsTickOrdered(command.ClientTick)
                || !context.IsSequenceOrdered(command.ClientTick, command.Sequence))
            {
                return NetworkActionResult.Reject(
                    NetworkActionResultCode.OutOfOrder,
                    context.ServerTick,
                    command.Sequence,
                    command.PredictionKey);
            }

            if (!context.IsTickInAcceptedWindow(command.ClientTick))
            {
                return NetworkActionResult.Reject(
                    NetworkActionResultCode.Expired,
                    context.ServerTick,
                    command.Sequence,
                    command.PredictionKey);
            }

            if (!context.AllowsInputMask(command.InputMask) || !context.AllowsCustomFlags(command.CustomFlags))
            {
                return NetworkActionResult.Reject(
                    NetworkActionResultCode.InvalidPayload,
                    context.ServerTick,
                    command.Sequence,
                    command.PredictionKey);
            }

            return NetworkActionResult.Accept(
                context.ServerTick,
                command.Sequence,
                command.PredictionKey,
                payloadHash: command.PayloadHash);
        }
    }
}
