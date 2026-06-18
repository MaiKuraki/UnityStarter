namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public enum InteractionValidationFailure
    {
        None = 0,
        InvalidRequest = 1,
        WrongWorld = 2,
        MissingInstigatorStableId = 3,
        MissingTargetStableId = 4,
        UnknownTarget = 5,
        TargetUnavailable = 6,
        ActionNotAllowed = 7,
        OutOfRange = 8,
        DuplicateRequest = 9,
        RateLimited = 10,
        QueueFull = 11,
        TooManyQueuedForInstigator = 12,
        TickTooOld = 13,
        TickTooFarInFuture = 14,
        RequestHistoryFull = 15,
        Count = 16
    }
}
