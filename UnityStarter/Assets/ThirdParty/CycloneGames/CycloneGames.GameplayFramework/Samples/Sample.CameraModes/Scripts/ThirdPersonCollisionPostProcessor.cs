using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Camera post-processor that prevents the camera from clipping through geometry (spring-arm probe).
    ///
    /// <para><b>How it works</b>
    /// Each frame a sphere-cast is fired from a pivot point (the view target's position offset upward)
    /// toward the desired camera position. If the probe hits geometry the camera is pulled in to the
    /// safe distance instantly, preventing clip-through. When the path clears the camera recovers
    /// smoothly at <see cref="RecoverySpeed"/> units per second.</para>
    ///
    /// <para><b>Usage</b>
    /// <code>
    /// var collision = new ThirdPersonCollisionPostProcessor
    /// {
    ///     PivotHeightOffset = 1.5f,
    ///     ProbeRadius       = 0.15f,
    ///     CollisionMask     = LayerMask.GetMask("Default", "Terrain"),
    ///     RecoverySpeed     = 6f,
    /// };
    /// cameraManager.RegisterPostProcessor(collision);
    ///
    /// // When the CameraManager is destroyed or mode changes:
    /// cameraManager.UnregisterPostProcessor(collision);
    /// </code></para>
    ///
    /// <para><b>Notes</b>
    /// • <see cref="PivotHeightOffset"/> should roughly match the <c>PivotHeight</c> set on your
    ///   <see cref="ThirdPersonFollowCameraMode"/> so the probe origin aligns with the camera orbit
    ///   center.
    /// • Set <see cref="ProbeRadius"/> to at least the camera's near-clip plane distance to avoid
    ///   residual clip artefacts.
    /// • Register <i>after</i> any shake or offset processors so collision resolves the final pose.</para>
    /// </summary>
    public sealed class ThirdPersonCollisionPostProcessor : ICameraPostProcessor
    {
        // Shared hit buffer to keep this post-processor allocation free.
        private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[16];

        // ─── Configuration ───────────────────────────────────────────────────

        /// <summary>
        /// Height above the view target's origin used as the sphere-cast pivot.
        /// Should match <see cref="ThirdPersonFollowCameraMode.PivotHeight"/>.
        /// </summary>
        public float PivotHeightOffset { get; set; } = 1.5f;

        /// <summary>
        /// Radius of the probe sphere in world units. Match to the camera near-clip tolerance.
        /// </summary>
        public float ProbeRadius { get; set; } = 0.15f;

        /// <summary>
        /// Physics layers to probe against. Defaults to everything.
        /// Typically exclude the player's own character layer and trigger volumes.
        /// </summary>
        public LayerMask CollisionMask { get; set; } = ~0;

        /// <summary>
        /// How quickly (units/sec) the camera recovers to the desired distance after occlusion clears.
        /// Higher values feel snappier; lower values give a smoother return.
        /// </summary>
        public float RecoverySpeed { get; set; } = 6f;

        /// <summary>
        /// When enabled, camera pull-in is speed-limited instead of instant.
        /// Disable to guarantee zero visual penetration at the cost of hard snaps.
        /// </summary>
        public bool SmoothCompression { get; set; } = false;

        /// <summary>
        /// Max pull-in speed (units/sec) while blocked, when <see cref="SmoothCompression"/> is enabled.
        /// </summary>
        public float CompressionSpeed { get; set; } = 20f;

        /// <summary>
        /// Ignore very small occluders to reduce camera jitter from tiny props.
        /// </summary>
        public bool IgnoreSmallOccluders { get; set; } = true;

        /// <summary>
        /// Minimum collider bounds magnitude to be considered a meaningful occluder.
        /// </summary>
        public float MinOccluderBoundsMagnitude { get; set; } = 0.4f;

        /// <summary>
        /// Colliders with this tag are always ignored as camera blockers.
        /// </summary>
        public string IgnoreOccluderTag { get; set; } = "CameraIgnoreOccluder";

        /// <summary>
        /// Enforces a ground clearance so the camera does not dip underground on sharp terrain.
        /// </summary>
        public bool GroundClampEnabled { get; set; } = true;

        /// <summary>
        /// Ground layers used by the downward probe.
        /// </summary>
        public LayerMask GroundMask { get; set; } = ~0;

        /// <summary>
        /// Minimum world-space clearance above detected ground.
        /// </summary>
        public float GroundClearance { get; set; } = 0.05f;

        /// <summary>
        /// Height above candidate camera position used to start the downward ground probe.
        /// </summary>
        public float GroundProbeUpDistance { get; set; } = 1.5f;

        /// <summary>
        /// Max distance of the downward ground probe.
        /// </summary>
        public float GroundProbeDistance { get; set; } = 4f;

        // ─── Internal state ──────────────────────────────────────────────────

        // Negative sentinel: not yet initialized.
        private float _currentDistance = -1f;

        // ─── Public API ──────────────────────────────────────────────────────

        /// <summary>Reset internal state (e.g. after a teleport or possession change).</summary>
        public void Reset() { _currentDistance = -1f; }

        // ─── ICameraPostProcessor ────────────────────────────────────────────

        public CameraPose Process(CameraPose desiredPose, CameraContext context, float deltaTime)
        {
            Actor viewTarget = context?.CurrentViewTarget;
            if (viewTarget == null) return desiredPose;

            Vector3 pivot          = viewTarget.GetActorLocation() + Vector3.up * PivotHeightOffset;
            Vector3 desiredOffset  = desiredPose.Position - pivot;
            float   desiredDistance = desiredOffset.magnitude;

            if (desiredDistance < 0.001f) return desiredPose;

            // Lazy init: snap to desired distance on the first frame to avoid a pop.
            if (_currentDistance < 0f) _currentDistance = desiredDistance;

            Vector3 probeDir = desiredOffset / desiredDistance;

            bool blocked = TryFindBlockingHit(pivot, probeDir, desiredDistance, out RaycastHit hit);

            if (blocked)
            {
                // Pull in immediately — never interpolate into geometry.
                // Subtract probe radius so the sphere surface sits at the hit point.
                float safeDistance = Mathf.Max(ProbeRadius, hit.distance - ProbeRadius);
                if (!SmoothCompression)
                {
                    _currentDistance = Mathf.Min(_currentDistance, safeDistance);
                }
                else
                {
                    float targetDistance = Mathf.Min(_currentDistance, safeDistance);
                    _currentDistance = Mathf.MoveTowards(_currentDistance, targetDistance, CompressionSpeed * deltaTime);
                }
            }
            else
            {
                // Recover toward the desired distance smoothly.
                _currentDistance = Mathf.MoveTowards(
                    _currentDistance, desiredDistance, RecoverySpeed * deltaTime);
            }

            Vector3 safePosition = pivot + probeDir * _currentDistance;
            if (GroundClampEnabled)
            {
                safePosition = ClampAboveGround(safePosition);
            }

            return new CameraPose(safePosition, desiredPose.Rotation, desiredPose.Fov);
        }

        private bool TryFindBlockingHit(Vector3 pivot, Vector3 direction, float distance, out RaycastHit blockingHit)
        {
            int hitCount = Physics.SphereCastNonAlloc(
                pivot, ProbeRadius, direction, s_hitBuffer, distance, CollisionMask, QueryTriggerInteraction.Ignore);

            if (hitCount <= 0)
            {
                blockingHit = default;
                return false;
            }

            SortHitsByDistance(s_hitBuffer, hitCount);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = s_hitBuffer[i];
                Collider collider = hit.collider;
                if (collider == null) continue;
                if (ShouldIgnoreOccluder(collider)) continue;

                blockingHit = hit;
                return true;
            }

            blockingHit = default;
            return false;
        }

        private bool ShouldIgnoreOccluder(Collider collider)
        {
            if (collider == null) return true;

            if (!string.IsNullOrEmpty(IgnoreOccluderTag) && collider.CompareTag(IgnoreOccluderTag))
            {
                return true;
            }

            if (IgnoreSmallOccluders)
            {
                float boundsMagnitude = collider.bounds.size.magnitude;
                if (boundsMagnitude < MinOccluderBoundsMagnitude)
                {
                    return true;
                }
            }

            return false;
        }

        private Vector3 ClampAboveGround(Vector3 candidatePosition)
        {
            Vector3 probeStart = candidatePosition + Vector3.up * Mathf.Max(0.01f, GroundProbeUpDistance);
            if (Physics.Raycast(
                    probeStart,
                    Vector3.down,
                    out RaycastHit groundHit,
                    GroundProbeDistance,
                    GroundMask,
                    QueryTriggerInteraction.Ignore))
            {
                float minY = groundHit.point.y + GroundClearance;
                if (candidatePosition.y < minY)
                {
                    candidatePosition.y = minY;
                }
            }

            return candidatePosition;
        }

        private static void SortHitsByDistance(RaycastHit[] hits, int hitCount)
        {
            for (int i = 1; i < hitCount; i++)
            {
                RaycastHit key = hits[i];
                float keyDistance = key.distance;
                int j = i - 1;
                while (j >= 0 && hits[j].distance > keyDistance)
                {
                    hits[j + 1] = hits[j];
                    j--;
                }

                hits[j + 1] = key;
            }
        }
    }
}
