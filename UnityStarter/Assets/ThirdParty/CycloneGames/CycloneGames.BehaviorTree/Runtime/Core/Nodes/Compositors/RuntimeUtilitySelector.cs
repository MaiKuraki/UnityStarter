using System;

namespace CycloneGames.BehaviorTree.Core.Nodes.Compositors
{
    /// <summary>
    /// Selects the highest-scoring child on each activation. Score keys are copied at setup;
    /// ties resolve to the lowest child index, and selection reads the blackboard without allocation.
    /// </summary>
    public class RuntimeUtilitySelector : RuntimeCompositeNode
    {
        private int _selectedChild;
        private int[] _scoreKeys = Array.Empty<int>();

        public override int CurrentIndex => _selectedChild;

        /// <summary>
        /// Configure the BB key hashes whose float values act as child scores.
        /// scoreKeys[i] corresponds to Children[i]. Length must match child count.
        /// </summary>
        public void SetScoreKeys(int[] scoreKeys)
        {
            ThrowIfSetupFrozen();
            if (IsStarted)
            {
                throw new InvalidOperationException(
                    "Utility selector score keys cannot change during an active execution.");
            }

            if (scoreKeys == null || scoreKeys.Length == 0)
            {
                _scoreKeys = Array.Empty<int>();
                return;
            }

            _scoreKeys = new int[scoreKeys.Length];
            Array.Copy(scoreKeys, _scoreKeys, scoreKeys.Length);
        }

        protected override void ValidateSetup()
        {
            if (_scoreKeys.Length != ChildCount)
            {
                throw new InvalidOperationException(
                    $"Utility selector score-key count ({_scoreKeys.Length}) must match child count ({ChildCount}).");
            }

            for (int i = 0; i < _scoreKeys.Length; i++)
            {
                if (_scoreKeys[i] == 0)
                {
                    throw new InvalidOperationException($"Utility selector score key[{i}] cannot be zero.");
                }
            }
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _selectedChild = -1;

            RuntimeNode[] children = ChildArray;
            for (int i = 0; i < children.Length; i++)
                children[i].PrepareForActivation();

            float bestScore = float.MinValue;
            int keyCount = _scoreKeys.Length;

            for (int i = 0; i < children.Length; i++)
            {
                float score;
                if (i < keyCount && _scoreKeys[i] != 0)
                {
                    score = blackboard.GetFloat(_scoreKeys[i]);
                }
                else
                {
                    score = 0f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    _selectedChild = i;
                }
            }

            if (_selectedChild < 0 && children.Length > 0)
                _selectedChild = 0;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = ChildArray;
            if (children == null || children.Length == 0 || _selectedChild < 0 || _selectedChild >= children.Length)
                return RuntimeState.Failure;

            return children[_selectedChild].Run(blackboard);
        }

        protected override void OnExit(RuntimeBlackboard blackboard, RuntimeNodeExitReason reason, System.Exception exception)
        {
            RuntimeNode[] children = ChildArray;
            if (children != null && _selectedChild >= 0 && _selectedChild < children.Length
                && children[_selectedChild].IsStarted)
            {
                children[_selectedChild].Abort(blackboard);
            }
        }
    }
}
