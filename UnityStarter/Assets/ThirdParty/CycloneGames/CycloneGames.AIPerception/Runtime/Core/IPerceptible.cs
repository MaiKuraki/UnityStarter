using System.Collections.Generic;
using System;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Stable perceptible type catalog. Product code registers project-owned custom type IDs.
    /// </summary>
    public static class PerceptibleTypes
    {
        // Built-in types
        public const int Default = 0;
        public const int Player = 1;
        public const int Enemy = 2;
        public const int Ally = 3;
        public const int Neutral = 4;
        public const int Interactable = 5;
        public const int SoundSource = 6;

        private static readonly Dictionary<int, string> _typeNames = new Dictionary<int, string>();

        static PerceptibleTypes()
        {
            ResetCatalog();
        }

        /// <summary>
        /// Registers a stable, project-owned custom type ID. Re-registering the same ID and name is
        /// idempotent; reusing an ID for a different name fails.
        /// </summary>
        /// <param name="id">The persistent custom type ID. Values below 100 are reserved.</param>
        /// <param name="name">The non-empty diagnostic name associated with the ID.</param>
        public static void RegisterType(int id, string name)
        {
            if (id < 100)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Custom perceptible type IDs must be at least 100.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Perceptible type name must not be empty.", nameof(name));
            }

            if (_typeNames.TryGetValue(id, out string existing))
            {
                if (!string.Equals(existing, name, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Perceptible type ID {id} is already registered as '{existing}'.");
                }

                return;
            }

            _typeNames.Add(id, name);
        }

        /// <summary>
        /// Gets the name of a type by ID.
        /// </summary>
        public static string GetTypeName(int typeId)
        {
            return _typeNames.TryGetValue(typeId, out var name) ? name : $"Type_{typeId}";
        }

        /// <summary>
        /// Gets all registered type IDs.
        /// </summary>
        public static IEnumerable<int> GetAllTypes() => _typeNames.Keys;

        public static bool IsRegistered(int typeId) => _typeNames.ContainsKey(typeId);

        private static void ResetCatalog()
        {
            _typeNames.Clear();
            _typeNames.Add(Default, "Default");
            _typeNames.Add(Player, "Player");
            _typeNames.Add(Enemy, "Enemy");
            _typeNames.Add(Ally, "Ally");
            _typeNames.Add(Neutral, "Neutral");
            _typeNames.Add(Interactable, "Interactable");
            _typeNames.Add(SoundSource, "SoundSource");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResetCatalog();
        }
    }

    public interface IPerceptible
    {
        int PerceptibleId { get; }
        int PerceptibleTypeId { get; }
        bool IsDetectable { get; }
        float3 Position { get; }
        float DetectionRadius { get; }
        float Loudness { get; }
        bool IsSoundSource { get; }
        float3 GetLOSPoint();

        // Optional: Custom tag for filtering
        string Tag { get; }
    }
}
