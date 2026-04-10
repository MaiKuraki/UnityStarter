namespace CycloneGames.GameplayFramework.Runtime
{
    public abstract class CameraMode
    {
        public virtual float BlendDuration => 0.2f;

        public virtual void OnActivate(CameraContext context) { }

        public virtual void OnDeactivate(CameraContext context) { }

        public virtual void Tick(CameraContext context, float deltaTime) { }

        public abstract CameraPose Evaluate(CameraContext context, in CameraPose basePose, float deltaTime);
    }
}