using System;
using CycloneGames.RPGFoundation.Trajectory.Core;
using UnityEngine;
using CycloneGames.Compat;

namespace CycloneGames.RPGFoundation.Trajectory.Runtime
{
    public class TrajectoryDebugProbe : MonoBehaviour
    {
        [SerializeField] private TrajectoryQueryPresetAsset QueryPreset;
        [SerializeField] private Transform OriginOverride;
        [SerializeField] private Vector3 LocalDirection = Vector3.forward;
        [SerializeField] private TrajectoryCollisionMode CollisionMode = TrajectoryCollisionMode.Physics3D;
        [SerializeField] private LayerMask ReflectionLayerMask;
        [SerializeField] private LayerMask PierceLayerMask;
        [SerializeField] private QueryTriggerInteraction QueryTriggerInteraction = QueryTriggerInteraction.Ignore;
        [SerializeField] private int SegmentCapacity = 16;
        [SerializeField] private int HitCapacity = 16;
        [SerializeField] private int CastHitCapacity = 16;
        [SerializeField] private bool DrawScenePreview = true;
        [SerializeField] private Color SegmentColor = new Color(0.25f, 0.85f, 1f, 1f);
        [SerializeField] private Color ReflectionColor = new Color(1f, 0.75f, 0.25f, 1f);
        [SerializeField] private Color PierceColor = new Color(0.65f, 0.45f, 1f, 1f);
        [SerializeField] private Color HitColor = new Color(1f, 0.25f, 0.25f, 1f);
        [SerializeField] private Color NormalColor = new Color(0.35f, 1f, 0.45f, 1f);

        private TrajectoryTraceBuffer _buffer;
        private ITrajectoryCollisionWorld _collisionWorld;
        private int _bufferSegmentCapacity;
        private int _bufferHitCapacity;
        private int _bufferCastHitCapacity;
        private TrajectoryCollisionMode _collisionMode;
        private int _reflectionLayerMask;
        private int _pierceLayerMask;
        private int _collisionCapacity;
        private QueryTriggerInteraction _queryTriggerInteraction;
        private bool _queriesHitTriggers;
        private bool _collisionWorldCreated;

        /// <summary>Layer mask that maps Unity hits to Reflect. Empty means every hit stops the trace.</summary>
        public LayerMask ReflectionMask
        {
            get
            {
                return ReflectionLayerMask;
            }
        }

        /// <summary>Layer mask that maps Unity hits to Pierce. Empty means every hit stops the trace.</summary>
        public LayerMask PierceMask
        {
            get
            {
                return PierceLayerMask;
            }
        }

        public TrajectoryQueryPresetAsset Preset
        {
            get
            {
                return QueryPreset;
            }
        }

        /// <summary>
        /// Transform that supplies the trace origin, or null when the probe's own transform is used.
        /// Scene handles edit this transform so authoring stays undoable.
        /// </summary>
        public Transform OriginOverrideTransform
        {
            get
            {
                return OriginOverride;
            }
        }

        /// <summary>
        /// Re-aims the probe from a world-space direction, converting it into local space so the
        /// serialized <c>LocalDirection</c> stays meaningful when the transform is rotated.
        /// </summary>
        public void SetLocalDirectionFromWorld(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            LocalDirection = transform.InverseTransformDirection(worldDirection.normalized);
        }

        public bool ScenePreviewEnabled
        {
            get
            {
                return DrawScenePreview;
            }
        }

        public Color PreviewSegmentColor
        {
            get
            {
                return SegmentColor;
            }
        }

        public Color PreviewReflectionColor
        {
            get
            {
                return ReflectionColor;
            }
        }

        public Color PreviewPierceColor
        {
            get
            {
                return PierceColor;
            }
        }

        public Color PreviewHitColor
        {
            get
            {
                return HitColor;
            }
        }

        public Color PreviewNormalColor
        {
            get
            {
                return NormalColor;
            }
        }

        public TrajectoryTraceBuffer Buffer
        {
            get
            {
                EnsureBuffer();
                return _buffer;
            }
        }

        public Vector3 Origin
        {
            get
            {
                return OriginOverride != null ? OriginOverride.position : transform.position;
            }
        }

        public Vector3 Direction
        {
            get
            {
                Vector3 direction = transform.TransformDirection(LocalDirection);
                if (direction.sqrMagnitude <= 0.000001f)
                {
                    direction = transform.forward;
                }

                return direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.forward;
            }
        }

        public bool TryTrace(out TrajectoryTraceResult result)
        {
            result = default;
            if (QueryPreset == null)
            {
                return false;
            }

            EnsureBuffer();
            EnsureCollisionWorld();

            // A custom adapter can be a MonoBehaviour. An interface reference comparison cannot see
            // Unity's fake-null for a destroyed object, so check the Unity object explicitly before
            // dispatching. This mirrors the identity contract documented on ITrajectoryCollisionWorld.
            if (_collisionWorld is UnityEngine.Object unityAdapter && unityAdapter == null)
            {
                return false;
            }

            TrajectoryQuery query = QueryPreset.BuildQuery(
                traceId: unchecked((int)UnityObjectId.Of(this)),
                ownerEntityId: 0UL,
                Origin,
                Direction);

            result = TrajectorySolver.Trace(in query, _collisionWorld, _buffer);
            return true;
        }

        protected virtual void EnsureBuffer()
        {
            int segmentCapacity = Math.Max(1, SegmentCapacity);
            int hitCapacity = Math.Max(1, HitCapacity);
            int castHitCapacity = Math.Max(1, CastHitCapacity);
            if (_buffer != null
                && _bufferSegmentCapacity == segmentCapacity
                && _bufferHitCapacity == hitCapacity
                && _bufferCastHitCapacity == castHitCapacity)
            {
                return;
            }

            _buffer = new TrajectoryTraceBuffer(segmentCapacity, hitCapacity, castHitCapacity);
            _bufferSegmentCapacity = segmentCapacity;
            _bufferHitCapacity = hitCapacity;
            _bufferCastHitCapacity = castHitCapacity;
        }

        protected virtual void EnsureCollisionWorld()
        {
            int reflectionMask = ReflectionLayerMask.value;
            int pierceMask = PierceLayerMask.value;
            int capacity = Math.Max(1, CastHitCapacity);

            // The 2D adapter snapshots this global into its ContactFilter2D when constructed, so a
            // change to it must invalidate the cached world.
            bool queriesHitTriggers = Physics2D.queriesHitTriggers;

            if (_collisionWorldCreated
                && _collisionMode == CollisionMode
                && _reflectionLayerMask == reflectionMask
                && _pierceLayerMask == pierceMask
                && _collisionCapacity == capacity
                && _queryTriggerInteraction == QueryTriggerInteraction
                && _queriesHitTriggers == queriesHitTriggers)
            {
                return;
            }

            _collisionWorld = CreateCollisionWorld(capacity);
            _collisionWorldCreated = true;
            _collisionMode = CollisionMode;
            _reflectionLayerMask = reflectionMask;
            _pierceLayerMask = pierceMask;
            _collisionCapacity = capacity;
            _queryTriggerInteraction = QueryTriggerInteraction;
            _queriesHitTriggers = queriesHitTriggers;
        }

        protected virtual ITrajectoryCollisionWorld CreateCollisionWorld(int capacity)
        {
            switch (CollisionMode)
            {
                case TrajectoryCollisionMode.Physics3D:
                    return new UnityTrajectoryCollisionWorld3D(
                        capacity,
                        ReflectionLayerMask.value,
                        PierceLayerMask.value,
                        QueryTriggerInteraction);
                case TrajectoryCollisionMode.Physics2D:
                    return new UnityTrajectoryCollisionWorld2D(
                        capacity,
                        ReflectionLayerMask.value,
                        PierceLayerMask.value);
                default:
                    return null;
            }
        }
    }
}
