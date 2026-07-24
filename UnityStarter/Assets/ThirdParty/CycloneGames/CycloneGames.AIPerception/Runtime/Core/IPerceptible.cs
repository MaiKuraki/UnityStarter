using System.Collections.Generic;
using System;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Extensible perceptible type system. Developers can register custom types at runtime.
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

        private static int _nextCustomType = 100;

        static PerceptibleTypes()
        {
            ResetCatalog();
        }

        /// <summary>
        /// Registers a custom type and returns its ID.
        /// </summary>
        [Obsolete("Process-order type IDs are not stable across saves or peers. Use RegisterType(int, string).")]
        public static int RegisterType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Perceptible type name must not be empty.", nameof(name));
            }

            int id = _nextCustomType++;
            _typeNames[id] = name;
            return id;
        }

        /// <summary>
        /// Registers a stable project-owned type ID. Save and wire contracts must use this form,
        /// never the process-order-dependent <see cref="RegisterType(string)"/> overload.
        /// </summary>
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
            if (id >= _nextCustomType)
            {
                _nextCustomType = id + 1;
            }
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
            _nextCustomType = 100;
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
