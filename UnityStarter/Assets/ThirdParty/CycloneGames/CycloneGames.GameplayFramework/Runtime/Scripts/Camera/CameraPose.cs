using System;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    public readonly struct CameraPose
    {
        private const float MIN_ROTATION_SQR_MAGNITUDE = 0.000000000001f;

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float Fov { get; }
        public bool IsValid =>
            IsFinite(Position) &&
            IsValidRotation(Rotation) &&
            IsValidFov(Fov);

        public CameraPose(Vector3 position, Quaternion rotation, float fov)
        {
            if (!IsFinite(position))
            {
                throw new ArgumentException(
                    "Camera position must contain only finite values.",
                    nameof(position));
            }

            if (!TryNormalizeRotation(rotation, out Quaternion normalizedRotation))
            {
                throw new ArgumentException(
                    "Camera rotation must be finite and non-degenerate.",
                    nameof(rotation));
            }

            if (!IsValidFov(fov))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fov),
                    "Camera field of view must be finite and strictly between 0 and 180 degrees.");
            }

            Position = position;
            Rotation = normalizedRotation;
            Fov = fov;
        }

        private CameraPose(
            Vector3 position,
            Quaternion normalizedRotation,
            float fov,
            bool skipValidation)
        {
            Position = position;
            Rotation = normalizedRotation;
            Fov = fov;
        }

        public static bool TryCreate(
            Vector3 position,
            Quaternion rotation,
            float fov,
            out CameraPose pose)
        {
            if (!IsFinite(position) ||
                !TryNormalizeRotation(rotation, out Quaternion normalizedRotation) ||
                !IsValidFov(fov))
            {
                pose = default;
                return false;
            }

            pose = new CameraPose(
                position,
                normalizedRotation,
                fov,
                skipValidation: true);
            return true;
        }

        public CameraPose WithFov(float fov)
        {
            return new CameraPose(Position, Rotation, fov);
        }

        public static CameraPose Lerp(in CameraPose fromPose, in CameraPose toPose, float t)
        {
            if (!fromPose.IsValid)
            {
                throw new ArgumentException("The source camera pose is invalid.", nameof(fromPose));
            }

            if (!toPose.IsValid)
            {
                throw new ArgumentException("The target camera pose is invalid.", nameof(toPose));
            }

            if (!IsFinite(t))
            {
                throw new ArgumentOutOfRangeException(nameof(t), "Interpolation progress must be finite.");
            }

            return new CameraPose(
                Vector3.LerpUnclamped(fromPose.Position, toPose.Position, t),
                Quaternion.SlerpUnclamped(fromPose.Rotation, toPose.Rotation, t),
                Mathf.LerpUnclamped(fromPose.Fov, toPose.Fov, t));
        }

        private static bool TryNormalizeRotation(
            Quaternion rotation,
            out Quaternion normalizedRotation)
        {
            if (!IsValidRotation(rotation))
            {
                normalizedRotation = default;
                return false;
            }

            float squaredMagnitude =
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w;
            float inverseMagnitude = 1f / Mathf.Sqrt(squaredMagnitude);
            normalizedRotation = new Quaternion(
                rotation.x * inverseMagnitude,
                rotation.y * inverseMagnitude,
                rotation.z * inverseMagnitude,
                rotation.w * inverseMagnitude);
            return true;
        }

        private static bool IsValidRotation(Quaternion rotation)
        {
            if (!IsFinite(rotation.x) ||
                !IsFinite(rotation.y) ||
                !IsFinite(rotation.z) ||
                !IsFinite(rotation.w))
            {
                return false;
            }

            float squaredMagnitude =
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w;
            return IsFinite(squaredMagnitude) &&
                   squaredMagnitude > MIN_ROTATION_SQR_MAGNITUDE;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsValidFov(float fov)
        {
            return IsFinite(fov) && fov > 0f && fov < 180f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
