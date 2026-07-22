using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Utility AI selector: scores each child via blackboard keys and executes the highest-scoring one.
    /// 
    /// Each child has a corresponding BB key (int hash) whose float value is used as the score.
    /// On each activation, all scores are read from the blackboard and the child with the
    /// highest score is selected. Ties are broken by index (first wins).
    /// 
    /// Score keys are copied during setup so caller-owned arrays cannot mutate an owned tree.
    /// Runtime selection reads directly from the blackboard without per-activation allocations.
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

            // Read scores from BB and pick highest
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
