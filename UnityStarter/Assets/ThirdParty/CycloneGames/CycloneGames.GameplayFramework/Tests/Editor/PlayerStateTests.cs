using System.Collections.Generic;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class PlayerStateTests
    {
        private readonly List<GameObject> objects = new List<GameObject>(4);

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }

            objects.Clear();
        }

        [Test]
        public void ToDataObjectAndFromDataObject_RoundTripFrameworkIdentity()
        {
            PlayerState source = CreatePlayerState("Source");
            source.SetPlayerName("PlayerOne");
            source.SetPlayerId(42);
            source.SetIsSpectator(true);

            PlayerStateData data = source.ToDataObject();
            PlayerState target = CreatePlayerState("Target");
            target.FromDataObject(data);

            Assert.AreEqual("PlayerOne", target.GetPlayerName());
            Assert.AreEqual(42, target.GetPlayerId());
            Assert.IsTrue(target.IsSpectator());
        }

        [Test]
        public void SerializeAndDeserialize_RoundTripFrameworkIdentity()
        {
            PlayerState source = CreatePlayerState("Source");
            source.SetPlayerName("SerializedPlayer");
            source.SetPlayerId(77);
            source.SetIsSpectator(true);
            MemoryDataStore store = new MemoryDataStore();

            source.Serialize(store);
            PlayerState target = CreatePlayerState("Target");
            target.Deserialize(store);

            Assert.AreEqual("SerializedPlayer", target.GetPlayerName());
            Assert.AreEqual(77, target.GetPlayerId());
            Assert.IsTrue(target.IsSpectator());
        }

        [Test]
        public void CopyProperties_CopiesIdentityWithoutRequiringPawn()
        {
            PlayerState source = CreatePlayerState("Source");
            source.SetPlayerName("CopiedPlayer");
            source.SetPlayerId(11);
            source.SetIsSpectator(true);
            PlayerState target = CreatePlayerState("Target");

            target.CopyProperties(source);

            Assert.AreEqual("CopiedPlayer", target.GetPlayerName());
            Assert.AreEqual(11, target.GetPlayerId());
            Assert.IsTrue(target.IsSpectator());
        }

        [Test]
        public void SetPawnPrivate_FiresOnlyWhenPawnChanges()
        {
            PlayerState playerState = CreatePlayerState("PlayerState");
            Pawn pawn = CreateObject("Pawn").AddComponent<Pawn>();
            int eventCount = 0;
            Pawn lastNewPawn = null;
            Pawn lastOldPawn = null;

            playerState.OnPawnSetEvent += (_, newPawn, oldPawn) =>
            {
                eventCount++;
                lastNewPawn = newPawn;
                lastOldPawn = oldPawn;
            };

            playerState.SetPawnPrivate(pawn);
            playerState.SetPawnPrivate(pawn);
            playerState.SetPawnPrivate(null);

            Assert.AreEqual(2, eventCount);
            Assert.IsNull(lastNewPawn);
            Assert.AreSame(pawn, lastOldPawn);
            Assert.IsNull(playerState.GetPawn());
        }

        private PlayerState CreatePlayerState(string name)
        {
            return CreateObject(name).AddComponent<PlayerState>();
        }

        private GameObject CreateObject(string name)
        {
            GameObject gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject;
        }

        private sealed class MemoryDataStore : IDataWriter, IDataReader
        {
            private readonly Dictionary<string, object> values = new Dictionary<string, object>(8);

            public void WriteString(string key, string value) => values[key] = value;
            public void WriteInt(string key, int value) => values[key] = value;
            public void WriteFloat(string key, float value) => values[key] = value;
            public void WriteBool(string key, bool value) => values[key] = value;
            public void WriteDouble(string key, double value) => values[key] = value;
            public string ReadString(string key) => values.TryGetValue(key, out object value) ? (string)value : null;
            public int ReadInt(string key) => values.TryGetValue(key, out object value) ? (int)value : 0;
            public float ReadFloat(string key) => values.TryGetValue(key, out object value) ? (float)value : 0f;
            public bool ReadBool(string key) => values.TryGetValue(key, out object value) && (bool)value;
            public double ReadDouble(string key) => values.TryGetValue(key, out object value) ? (double)value : 0d;
        }
    }
}
