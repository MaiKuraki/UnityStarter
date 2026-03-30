namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Utility AI selector: scores each child via blackboard keys and executes the highest-scoring one.
    /// 
    /// Each child has a corresponding BB key (int hash) whose float value is used as the score.
    /// On each activation, all scores are read from the blackboard and the child with the
    /// highest score is selected. Ties are broken by index (first wins).
    /// 
    /// Design:
    /// - 0GC: pre-allocated float[] sized at Seal time
    /// - No delegate/virtual overhead: scores come directly from BB float keys
    /// - Thread-safe: uses BB's own locking (no additional locks needed)
    /// - Compatible with AIPerception: perception systems write scores to BB keys
    /// </summary>
    public class RuntimeUtilitySelector : RuntimeCompositeNode
    {
        private int _selectedChild;
        private float[] _scores;
        private int[] _scoreKeys;

        public override int CurrentIndex => _selectedChild;

        /// <summary>
        /// Configure the BB key hashes whose float values act as child scores.
        /// scoreKeys[i] corresponds to Children[i]. Length must match child count.
        /// </summary>
        public void SetScoreKeys(int[] scoreKeys)
        {
            _scoreKeys = scoreKeys;
        }

        public override void OnAwake()
        {
            base.OnAwake();
            _scores = new float[ChildCount];
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _selectedChild = -1;

            var children = Children;
            for (int i = 0; i < children.Length; i++)
                children[i].ResetState();

            // Read scores from BB and pick highest
            float bestScore = float.MinValue;
            int keyCount = _scoreKeys != null ? _scoreKeys.Length : 0;

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

                _scores[i] = score;

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
            var children = Children;
            if (children == null || children.Length == 0 || _selectedChild < 0 || _selectedChild >= children.Length)
                return RuntimeState.Failure;

            return children[_selectedChild].Run(blackboard);
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            var children = Children;
            if (children != null && _selectedChild >= 0 && _selectedChild < children.Length
                && children[_selectedChild].IsStarted)
            {
                children[_selectedChild].Abort(blackboard);
            }
        }
    }
}
