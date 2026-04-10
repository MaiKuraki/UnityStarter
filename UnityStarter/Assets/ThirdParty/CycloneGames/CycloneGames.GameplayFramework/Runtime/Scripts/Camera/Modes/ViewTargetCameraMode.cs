namespace CycloneGames.GameplayFramework.Runtime
{
    public sealed class ViewTargetCameraMode : CameraMode
    {
        public override CameraPose Evaluate(CameraContext context, in CameraPose basePose, float deltaTime)
        {
            return basePose;
        }
    }
}