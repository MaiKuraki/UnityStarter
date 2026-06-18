namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public readonly struct InteractionValidationResult
    {
        public readonly InteractionRequest Request;
        public readonly InteractionTargetSnapshot Target;
        public readonly InteractionValidationFailure Failure;
        public readonly int QueuePosition;

        private InteractionValidationResult(
            InteractionRequest request,
            InteractionTargetSnapshot target,
            InteractionValidationFailure failure,
            int queuePosition)
        {
            Request = request;
            Target = target;
            Failure = failure;
            QueuePosition = queuePosition > 0 ? queuePosition : 0;
        }

        public bool IsAccepted => Failure == InteractionValidationFailure.None;
        public bool IsQueued => QueuePosition > 0;

        public static InteractionValidationResult Accept(InteractionRequest request, InteractionTargetSnapshot target, int queuePosition = 0)
        {
            return new InteractionValidationResult(request, target, InteractionValidationFailure.None, queuePosition);
        }

        public static InteractionValidationResult Reject(InteractionRequest request, InteractionValidationFailure failure)
        {
            return new InteractionValidationResult(request, default, failure == InteractionValidationFailure.None ? InteractionValidationFailure.InvalidRequest : failure, 0);
        }
    }
}
