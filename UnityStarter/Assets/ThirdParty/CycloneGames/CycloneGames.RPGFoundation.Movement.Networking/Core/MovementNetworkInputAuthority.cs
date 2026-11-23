using System;
using CycloneGames.Networking.Maintenance;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public sealed class MovementNetworkInputAuthority
    {
        private readonly IMovementNetworkInputValidator _validator;
        private readonly MovementNetworkInputHistory _history;
        private readonly MovementTimeDilationAuditor _dilationAuditor;
        private readonly MovementSessionTeardownSet _teardownSet;

        public MovementNetworkInputAuthority(
            MovementNetworkInputHistory history,
            MovementTimeDilationAuditor dilationAuditor,
            IMovementNetworkInputValidator validator = null)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _dilationAuditor = dilationAuditor ?? throw new ArgumentNullException(nameof(dilationAuditor));
            _validator = validator ?? DefaultMovementNetworkInputValidator.Instance;
            _teardownSet = new MovementSessionTeardownSet(
                new MovementHistoryTeardownTarget(_history),
                new MovementAuditTeardownTarget(_dilationAuditor));
        }

        public MovementNetworkInputHistory History
        {
            get { return _history; }
        }

        public MovementTimeDilationAuditor DilationAuditor
        {
            get { return _dilationAuditor; }
        }

        public bool TryAccept(
            in MovementInputCommandMessage command,
            in MovementNetworkInputValidationContext context,
            out NetworkActionResult result)
        {
            if (context.Epoch == MovementWireIds.InvalidEpoch
                || command.Epoch != context.Epoch
                || !context.IsAuthenticated)
            {
                result = NetworkActionResult.Reject(
                    NetworkActionResultCode.Unauthorized,
                    context.ServerTick, command.InputSequence, command.PredictionKey);
                return false;
            }

            result = _validator.Validate(command, context);
            if (!result.IsAccepted) return false;

            if (_history.Contains(command))
            {
                result = NetworkActionResult.Reject(
                    NetworkActionResultCode.Duplicate,
                    context.ServerTick, command.InputSequence, command.PredictionKey);
                return false;
            }

            if (!_dilationAuditor.Audit(command.EntityId, command.DeltaTime, context.ServerTick.Value))
            {
                result = NetworkActionResult.Reject(
                    NetworkActionResultCode.RateLimited,
                    context.ServerTick, command.InputSequence, command.PredictionKey);
                return false;
            }

            _history.Record(command);
            return true;
        }

        public int ApplyAck(in MovementInputAckMessage ack)
        {
            if (!ack.IsValid) return 0;
            return _history.PruneUpTo(ack);
        }

        public MovementSessionTeardownSet GetTeardownSet()
        {
            return _teardownSet;
        }

        public void Reset()
        {
            _history.Clear();
            _dilationAuditor.Clear();
        }
    }
}
