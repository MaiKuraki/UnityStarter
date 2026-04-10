namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Post-processor applied to the final <see cref="CameraPose"/> after all
    /// <see cref="CameraMode"/> evaluations are complete.
    ///
    /// Register a processor with <see cref="CameraManager.RegisterPostProcessor"/>.
    /// Processors execute in registration order. Unregister when no longer needed to avoid
    /// stale references or incorrect behaviour after scene transitions.
    ///
    /// Common uses:
    ///   - Collision avoidance / spring-arm probe
    ///   - Procedural camera shake injection
    ///   - Post-process FOV curves (e.g. speed-based zoom)
    /// </summary>
    public interface ICameraPostProcessor
    {
        /// <summary>
        /// Transform the desired camera pose for this frame.
        /// </summary>
        /// <param name="desiredPose">Pose produced after all <see cref="CameraMode"/> evaluations.</param>
        /// <param name="context">Active camera context for the owning controller. May be null.</param>
        /// <param name="deltaTime">Frame delta time in seconds.</param>
        /// <returns>Adjusted pose passed to the next processor in the chain.</returns>
        CameraPose Process(CameraPose desiredPose, CameraContext context, float deltaTime);
    }
}
