using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class DamageEventTests
    {
        [Test]
        public void MakeGenericDamage_PreservesTypeAndDamageType()
        {
            TestDamageType damageType = new TestDamageType();

            DamageEvent damageEvent = DamageEvent.MakeGenericDamage(damageType);

            Assert.AreEqual(EDamageEventType.Generic, damageEvent.EventType);
            Assert.AreSame(damageType, damageEvent.DamageType);
        }

        [Test]
        public void MakePointDamage_PreservesPointFields()
        {
            TestDamageType damageType = new TestDamageType();
            Vector3 hitLocation = new Vector3(1f, 2f, 3f);
            Vector3 hitNormal = Vector3.up;
            Vector3 shotDirection = Vector3.forward;

            DamageEvent damageEvent = DamageEvent.MakePointDamage(hitLocation, hitNormal, shotDirection, damageType);

            Assert.AreEqual(EDamageEventType.Point, damageEvent.EventType);
            Assert.AreSame(damageType, damageEvent.DamageType);
            Assert.AreEqual(hitLocation, damageEvent.HitLocation);
            Assert.AreEqual(hitNormal, damageEvent.HitNormal);
            Assert.AreEqual(shotDirection, damageEvent.ShotDirection);
        }

        [Test]
        public void MakeRadialDamage_PreservesRadialFields()
        {
            TestDamageType damageType = new TestDamageType();
            Vector3 origin = new Vector3(4f, 5f, 6f);

            DamageEvent damageEvent = DamageEvent.MakeRadialDamage(origin, 2f, 8f, damageType);

            Assert.AreEqual(EDamageEventType.Radial, damageEvent.EventType);
            Assert.AreSame(damageType, damageEvent.DamageType);
            Assert.AreEqual(origin, damageEvent.Origin);
            Assert.AreEqual(2f, damageEvent.InnerRadius);
            Assert.AreEqual(8f, damageEvent.OuterRadius);
        }

        private sealed class TestDamageType : IDamageType
        {
            public bool CausedByWorld => false;
            public bool ScaleMomentumByMass => true;
            public float DamageImpulse => 800f;
            public float DamageFalloff => 1f;
        }
    }
}
