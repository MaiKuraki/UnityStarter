namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public class RuntimeCoolDownNode : RuntimeDecoratorNode
    {
        private float _coolDown;
        private bool _resetOnSuccess;

        public float CoolDown
        {
            get => _coolDown;
            set
            {
                ThrowIfSetupFrozen();
                ValidateFiniteNonNegativeSetupValue(value, nameof(CoolDown));
                _coolDown = value;
            }
        }

        public bool ResetOnSuccess
        {
            get => _resetOnSuccess;
            set => SetSetupValue(ref _resetOnSuccess, value);
        }

        private double _lastTime;
        private bool _hasCooldownBaseline;
        private bool _isCoolDownStarted;

        protected override void ValidateSetup()
        {
            ValidateFiniteNonNegativeSetupValue(_coolDown, nameof(CoolDown));
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            double currentTime = RuntimeBTTime.GetTime(blackboard, false);
            if (!_hasCooldownBaseline || currentTime - _lastTime >= CoolDown)
            {
                if (Child == null) return RuntimeState.Failure;
                _isCoolDownStarted = true;
                return Child.Run(blackboard);
            }
            return RuntimeState.Failure;
        }

        protected override void OnExit(RuntimeBlackboard blackboard, RuntimeNodeExitReason reason, System.Exception exception)
        {
            if (!_isCoolDownStarted) return;
            _isCoolDownStarted = false;

            if (Child != null && Child.IsStarted)
            {
                Child.Abort(blackboard);
            }

            if (!ResetOnSuccess || (Child != null && Child.State == RuntimeState.Success))
            {
                _lastTime = RuntimeBTTime.GetTime(blackboard, false);
                _hasCooldownBaseline = true;
            }
        }
    }
}
