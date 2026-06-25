using System;

using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public sealed class MovementNetworkInputAuthority
    {
        private readonly IMovementNetworkInputValidator _validator;
        private readonly MovementNetworkInputHistory _history;

        public MovementNetworkInputAuthority(
            MovementNetworkInputHistory history,
            IMovementNetworkInputValidator validator = null)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _validator = validator ?? DefaultMovementNetworkInputValidator.Instance;
        }

        public MovementNetworkInputHistory History
        {
            get
            {
                return _history;
            }
        }

        public bool TryAccept(
            in MovementInputCommandMessage command,
            in MovementNetworkInputValidationContext context,
            out NetworkActionResult result)
        {
            result = _validator.Validate(command, context);
            if (!result.IsAccepted)
            {
                return false;
            }

            if (_history.Contains(command))
            {
                result = NetworkActionResult.Reject(
                    NetworkActionResultCode.Duplicate,
                    context.ServerTick,
                    command.InputSequence,
                    command.PredictionKey);
                return false;
            }

            _history.Record(command);
            return true;
        }

        public void Reset()
        {
            _history.Clear();
        }
    }
}
