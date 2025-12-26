using System;
using UnityEngine;
using Unity.Burst;
using Unity.Mathematics;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class Actor : MonoBehaviour
    {
        [SerializeField] private float initialLifeSpanSec = 0;
        public event Action OwnerChanged;

        private Actor owner;
        public Actor GetOwner() => owner;
        public T GetOwner<T>() where T : Actor
        {
            return owner is T actor ? actor : null;
        }
        public void SetOwner(Actor NewOwner)
        {
            owner = NewOwner;
            OwnerChanged?.Invoke();
        }

        private string actorName;
        public string GetName() => actorName;
        public Vector3 GetActorLocation() => transform.position;
        public void SetActorPosition(Vector3 NewPosition)
        {
            if (transform.position != NewPosition)
            {
                transform.position = NewPosition;
            }
        }
        public float GetYaw() => transform.eulerAngles.y;
        public Quaternion GetActorRotation() => transform.rotation;

        /// <summary>
        /// Calculates Euler angles (pitch, yaw, roll) from quaternion using optimized math functions.
        /// Returns angles in degrees with XYZ axis order. Note: susceptible to gimbal lock.
        /// </summary>
        public Vector3 GetOrientation()
        {
            return QuaternionToEulerXYZ(transform.rotation);
        }

        /// <summary>
        /// Burst-compiled static method for quaternion to Euler XYZ conversion.
        /// Provides maximum performance for math-heavy operations.
        /// </summary>
        [BurstCompile]
        public static Vector3 QuaternionToEulerXYZ(Quaternion rotation)
        {
            //  wiki: https://en.wikipedia.org/wiki/Conversion_between_quaternions_and_Euler_angles
            //  unity Answers: https://answers.unity.com/questions/416169/finding-pitchrollyaw-from-quaternions.html
            quaternion q = rotation;
            float pitch = math.degrees(math.atan2(2f * q.value.x * q.value.w - 2f * q.value.y * q.value.z,
                                                  1f - 2f * q.value.x * q.value.x - 2f * q.value.z * q.value.z));
            float yaw = math.degrees(math.atan2(2f * q.value.y * q.value.w - 2f * q.value.x * q.value.z,
                                                1f - 2f * q.value.y * q.value.y - 2f * q.value.z * q.value.z));
            float roll = math.degrees(math.asin(math.clamp(2f * q.value.x * q.value.y + 2f * q.value.z * q.value.w, -1f, 1f)));
            return new Vector3(pitch, yaw, roll);
        }

        void SetLifeSpan(float newLifeSpan)
        {
            initialLifeSpanSec = newLifeSpan;

            if (initialLifeSpanSec > 0.001f)
            {
                Destroy(gameObject, initialLifeSpanSec);
            }
        }

        public virtual void FellOutOfWorld()
        {
            Destroy(gameObject);
        }

        public virtual void OutsideWorldBounds()
        {

        }

        protected virtual void Awake()
        {
            actorName = gameObject?.name;
        }

        protected virtual void Start()
        {
            SetLifeSpan(initialLifeSpanSec);

        }

        protected virtual void Update()
        {

        }

        protected virtual void LateUpdate()
        {

        }

        protected virtual void FixedUpdate()
        {

        }

        protected virtual void OnDestroy()
        {
            owner = null;
            OwnerChanged = null;
        }
    }
}