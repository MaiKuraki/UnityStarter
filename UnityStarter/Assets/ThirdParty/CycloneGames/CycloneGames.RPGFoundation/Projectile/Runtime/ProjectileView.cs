using CycloneGames.RPGFoundation.Projectile.Core;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Projectile.Runtime
{
    public sealed class ProjectileView : MonoBehaviour
    {
        public ProjectileHandle Handle { get; private set; }

        public void Initialize(ProjectileHandle handle, in ProjectileSnapshot snapshot)
        {
            Handle = handle;
            ApplySnapshot(in snapshot);
        }

        public void ApplySnapshot(in ProjectileSnapshot snapshot)
        {
            transform.position = new Vector3(
                snapshot.Position.X,
                snapshot.Position.Y,
                snapshot.Position.Z);

            Vector3 velocity = new Vector3(
                snapshot.Velocity.X,
                snapshot.Velocity.Y,
                snapshot.Velocity.Z);
            if (velocity.sqrMagnitude > 0.000001f)
            {
                transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
            }
        }

        public void ResetView()
        {
            Handle = default;
        }
    }
}
