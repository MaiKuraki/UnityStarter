namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions
{
    public sealed class RuntimeRandomChanceNode : RuntimeNode
    {
        private RuntimeDeterministicRandom _deterministicRandom;

        public RuntimeRandomChanceNode(float chance = 1f, float outOf = 1f, uint seed = 0u)
        {
            Chance = chance;
            OutOf = outOf;
            Seed = seed;
            _deterministicRandom = new RuntimeDeterministicRandom(seed);
        }

        public float Chance { get; set; }
        public float OutOf { get; set; }
        public uint Seed { get; set; }
        public string Name { get; set; }

        public override bool CanEvaluate => true;

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            if (OutOf <= 0f)
            {
                return false;
            }

            float threshold = Chance / OutOf;
            float value;
            if (Seed != 0u)
            {
                value = _deterministicRandom.NextFloat();
            }
            else
            {
                value = RuntimeRandomUtility.Range(blackboard, 0f, 1f);
            }

            return value <= threshold;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return Evaluate(blackboard) ? RuntimeState.Success : RuntimeState.Failure;
        }
    }
}
