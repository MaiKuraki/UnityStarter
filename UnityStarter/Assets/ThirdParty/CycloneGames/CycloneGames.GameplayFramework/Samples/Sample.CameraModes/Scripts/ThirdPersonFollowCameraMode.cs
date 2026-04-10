using UnityEngine;
using Unity.Mathematics;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Third-person follow camera mode with optional position lag, rotation lag, and angular dead zone.
    ///
    /// <para><b>Position lag</b> — the camera position spring-damps toward the desired position.
    /// Controls how quickly the camera catches up when the target moves (<see cref="PositionLagTime"/>).</para>
    ///
    /// <para><b>Rotation lag</b> — the camera rotates toward the look-at target at a limited rate
    /// (<see cref="RotationLagSpeed"/>), giving a cinematic trailing feel.</para>
    ///
    /// <para><b>Dead zone</b> — an angular rectangle around screen center. While the look-at target
    /// stays within the zone the camera does not rotate to re-center it, reducing camera noise during
    /// small movements. Only activates once the target leaves the rectangle.</para>
    ///
    /// <para>For collision avoidance register a <see cref="ThirdPersonCollisionPostProcessor"/> with
    /// the owning <see cref="CameraManager"/>.</para>
    /// </summary>
    public sealed class ThirdPersonFollowCameraMode : CameraMode
    {
        // ─── Basic geometry ─────────────────────────────────────────────────

        /// <summary>Distance behind the pivot to place the camera.</summary>
        public float FollowDistance { get; set; } = 4.0f;

        /// <summary>Height above actor origin used as the orbital pivot.</summary>
        public float PivotHeight    { get; set; } = 1.5f;

        /// <summary>Height above actor origin that the camera looks at.</summary>
        public float LookAtHeight   { get; set; } = 1.0f;

        /// <summary>FOV override. Set to 0 or negative to inherit from the base pose.</summary>
        public float OverrideFov    { get; set; } = 60.0f;

        // ─── Position lag ───────────────────────────────────────────────────

        /// <summary>When true the camera position spring-damps toward the desired position.</summary>
        public bool  PositionLagEnabled { get; set; } = false;

        /// <summary>SmoothDamp time in seconds — smaller values are snappier. Default 0.15 s.</summary>
        public float PositionLagTime    { get; set; } = 0.15f;

        // ─── Rotation lag ───────────────────────────────────────────────────

        /// <summary>When true the camera rotates toward the look-at target at a limited rate.</summary>
        public bool  RotationLagEnabled { get; set; } = false;

        /// <summary>
        /// Exponential decay speed for rotation lag (higher = snappier). Roughly: at speed 8 the
        /// camera covers ~95 % of the remaining angle in ~0.37 s.
        /// </summary>
        public float RotationLagSpeed   { get; set; } = 8.0f;

        // ─── Angular dead zone ──────────────────────────────────────────────

        /// <summary>When true the camera does not rotate while the look-at target is within the zone.</summary>
        public bool  DeadZoneEnabled    { get; set; } = false;

        /// <summary>Half-width of the dead zone in degrees (horizontal / yaw). Default 8°.</summary>
        public float DeadZoneHalfAngleX { get; set; } = 8.0f;

        /// <summary>Half-height of the dead zone in degrees (vertical / pitch). Default 6°.</summary>
        public float DeadZoneHalfAngleY { get; set; } = 6.0f;

        // ─── Internal persistent state ──────────────────────────────────────
        private Vector3    _laggedPosition;
        private Vector3    _lagVelocity;
        private Quaternion _laggedRotation;
        private bool       _stateInitialized;

        public override float BlendDuration => 0.25f;

        public override void OnActivate(CameraContext context)
        {
            // Force re-init on next Evaluate so lag state snaps to the current pose
            // rather than interpolating from a stale position.
            _stateInitialized = false;
        }

        public override void OnDeactivate(CameraContext context)
        {
            _stateInitialized = false;
        }

        public override CameraPose Evaluate(CameraContext context, in CameraPose basePose, float deltaTime)
        {
            Actor target = context != null ? context.CurrentViewTarget : null;
            if (target == null) return basePose;

            target.CalcCamera(deltaTime, out CameraPose targetPose, basePose.Fov);
            Vector3 pivot          = targetPose.Position + Vector3.up * PivotHeight;
            Vector3 desiredPosition = pivot + (targetPose.Rotation * Vector3.back) * FollowDistance;
            Vector3 lookAtPoint    = targetPose.Position + Vector3.up * LookAtHeight;

            // ── Initialize lag state on first evaluate so there is no pop ──
            if (!_stateInitialized)
            {
                _laggedPosition   = desiredPosition;
                _lagVelocity      = Vector3.zero;
                _laggedRotation   = basePose.Rotation.normalized;
                _stateInitialized = true;
            }

            // ── Position lag ────────────────────────────────────────────────
            Vector3 outputPosition;
            if (PositionLagEnabled && deltaTime > 0f)
            {
                _laggedPosition = Vector3.SmoothDamp(
                    _laggedPosition, desiredPosition, ref _lagVelocity,
                    PositionLagTime, Mathf.Infinity, deltaTime);
                outputPosition = _laggedPosition;
            }
            else
            {
                _laggedPosition = desiredPosition;
                outputPosition  = desiredPosition;
            }

            // ── Rotation toward look-at ─────────────────────────────────────
            Vector3 lookDir = lookAtPoint - outputPosition;
            Quaternion desiredRotation = CameraPoseMath.LookRotationSafe(
                (float3)lookDir,
                (quaternion)basePose.Rotation);

            // ── Dead zone check ─────────────────────────────────────────────
            // Project the direction-to-look-at into the camera's local space.
            // If both angular offsets are within the half-widths, suppress rotation.
            if (DeadZoneEnabled)
            {
                Quaternion referenceRot = RotationLagEnabled ? _laggedRotation : basePose.Rotation;
                if (CameraPoseMath.IsInsideAngularDeadZone(
                    (quaternion)referenceRot,
                    (float3)lookDir,
                    DeadZoneHalfAngleX,
                    DeadZoneHalfAngleY))
                {
                    // Target is inside the dead zone — hold the current rotation.
                    desiredRotation = referenceRot;
                }
            }

            // ── Rotation lag ────────────────────────────────────────────────
            Quaternion outputRotation;
            if (RotationLagEnabled && deltaTime > 0f)
            {
                // Exponential decay: frame-rate independent spring toward desired rotation.
                float lagT = CameraPoseMath.ExponentialDecayT(RotationLagSpeed, deltaTime);
                _laggedRotation = Quaternion.Slerp(_laggedRotation, desiredRotation, lagT);
                outputRotation  = _laggedRotation;
            }
            else
            {
                _laggedRotation = desiredRotation;
                outputRotation  = desiredRotation;
            }

            float fov = OverrideFov > 0f ? OverrideFov : basePose.Fov;
            return new CameraPose(outputPosition, outputRotation, fov);
        }
    }
}