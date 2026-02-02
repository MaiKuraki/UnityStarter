namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public enum InteractionStateType : byte
    {
        Idle = 0,
        Starting = 1,
        InProgress = 2,
        Completing = 3,
        Completed = 4,
        Cancelled = 5
    }

    // Flyweight state handlers for extensible state behavior
    public abstract class InteractionStateHandler
    {
        public abstract void OnEnter(IInteractable context);
        public abstract void OnExit(IInteractable context);
        public abstract bool CanTransitionTo(InteractionStateType nextState);
    }

    public sealed class InteractionStateHandler_Idle : InteractionStateHandler
    {
        public override void OnEnter(IInteractable context) { }
        public override void OnExit(IInteractable context) { }
        public override bool CanTransitionTo(InteractionStateType nextState) =>
            nextState == InteractionStateType.Starting;
    }

    public sealed class InteractionStateHandler_Starting : InteractionStateHandler
    {
        public override void OnEnter(IInteractable context) { }
        public override void OnExit(IInteractable context) { }
        public override bool CanTransitionTo(InteractionStateType nextState) =>
            nextState == InteractionStateType.InProgress ||
            nextState == InteractionStateType.Cancelled;
    }

    public sealed class InteractionStateHandler_InProgress : InteractionStateHandler
    {
        public override void OnEnter(IInteractable context) { }
        public override void OnExit(IInteractable context) { }
        public override bool CanTransitionTo(InteractionStateType nextState) =>
            nextState == InteractionStateType.Completing ||
            nextState == InteractionStateType.Cancelled;
    }

    public sealed class InteractionStateHandler_Completing : InteractionStateHandler
    {
        public override void OnEnter(IInteractable context) { }
        public override void OnExit(IInteractable context) { }
        public override bool CanTransitionTo(InteractionStateType nextState) =>
            nextState == InteractionStateType.Completed ||
            nextState == InteractionStateType.Cancelled;
    }

    public sealed class InteractionStateHandler_Completed : InteractionStateHandler
    {
        public override void OnEnter(IInteractable context) { }
        public override void OnExit(IInteractable context) { }
        public override bool CanTransitionTo(InteractionStateType nextState) =>
            nextState == InteractionStateType.Idle;
    }

    public sealed class InteractionStateHandler_Cancelled : InteractionStateHandler
    {
        public override void OnEnter(IInteractable context) { }
        public override void OnExit(IInteractable context) { }
        public override bool CanTransitionTo(InteractionStateType nextState) =>
            nextState == InteractionStateType.Idle;
    }

    public static class InteractionStateHandlers
    {
        public static readonly InteractionStateHandler Idle = new InteractionStateHandler_Idle();
        public static readonly InteractionStateHandler Starting = new InteractionStateHandler_Starting();
        public static readonly InteractionStateHandler InProgress = new InteractionStateHandler_InProgress();
        public static readonly InteractionStateHandler Completing = new InteractionStateHandler_Completing();
        public static readonly InteractionStateHandler Completed = new InteractionStateHandler_Completed();
        public static readonly InteractionStateHandler Cancelled = new InteractionStateHandler_Cancelled();

        private static readonly InteractionStateHandler[] _handlers =
        {
            Idle, Starting, InProgress, Completing, Completed, Cancelled
        };

        public static InteractionStateHandler GetHandler(InteractionStateType state) => _handlers[(int)state];
    }
}