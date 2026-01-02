using System.Collections.Generic;
using Unity.Mathematics;

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

        private static readonly Dictionary<int, string> _typeNames = new Dictionary<int, string>
        {
            { Default, "Default" },
            { Player, "Player" },
            { Enemy, "Enemy" },
            { Ally, "Ally" },
            { Neutral, "Neutral" },
            { Interactable, "Interactable" },
            { SoundSource, "SoundSource" }
        };

        private static int _nextCustomType = 100;

        /// <summary>
        /// Registers a custom type and returns its ID.
        /// </summary>
        public static int RegisterType(string name)
        {
            int id = _nextCustomType++;
            _typeNames[id] = name;
            return id;
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
    }

    public interface IPerceptible
    {
        int PerceptibleId { get; }
        int PerceptibleTypeId { get; } // Changed from enum to int for extensibility
        bool IsDetectable { get; }
        float3 Position { get; }
        float DetectionRadius { get; }
        float3 GetLOSPoint();

        // Optional: Custom tag for filtering
        string Tag { get; }
    }
}
