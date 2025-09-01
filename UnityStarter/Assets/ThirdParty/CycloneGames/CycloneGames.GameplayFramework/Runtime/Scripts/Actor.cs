using System;
using UnityEngine;

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
        void Orientation()
        {
            //  wiki: https://en.wikipedia.org/wiki/Conversion_between_quaternions_and_Euler_angles
            //  unity Answers: https://answers.unity.com/questions/416169/finding-pitchrollyaw-from-quaternions.html
            Quaternion q = transform.rotation;
            float pitch = Mathf.Rad2Deg * Mathf.Atan2(2 * q.x * q.w - 2 * q.y * q.z, 1 - 2 * q.x * q.x - 2 * q.z * q.z);
            float yaw = Mathf.Rad2Deg * Mathf.Atan2(2 * q.y * q.w - 2 * q.x * q.z, 1 - 2 * q.y * q.y - 2 * q.z * q.z);
            float roll = Mathf.Rad2Deg * Mathf.Asin(2 * q.x * q.y + 2 * q.z * q.w);
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
        }
    }    
}