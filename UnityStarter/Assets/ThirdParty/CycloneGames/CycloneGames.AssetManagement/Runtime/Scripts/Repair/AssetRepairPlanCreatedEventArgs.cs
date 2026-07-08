namespace CycloneGames.AssetManagement.Runtime
{
    public readonly struct AssetRepairPlanCreatedEventArgs
    {
        public readonly AssetRepairPlan Plan;

        public AssetRepairPlanCreatedEventArgs(AssetRepairPlan plan)
        {
            Plan = plan;
        }
    }
}
