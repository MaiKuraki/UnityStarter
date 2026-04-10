using System;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Attribute for Actor tag string fields. Enables a custom PropertyDrawer that shows
    /// a searchable popup of valid tags sourced from a constants class.
    /// 
    /// Usage:
    /// <code>
    /// public static class ActorTags
    /// {
    ///     public const string Player = "Player";
    ///     public const string Enemy  = "Enemy";
    ///     public const string NPC    = "NPC";
    /// }
    /// 
    /// [SerializeField, ActorTag(typeof(ActorTags))]
    /// private List<string> tags;
    /// </code>
    /// 
    /// When used without a type parameter, behaves as a normal string field:
    /// <code>
    /// [SerializeField, ActorTag]
    /// private List<string> tags;
    /// </code>
    /// </summary>
    public class ActorTagAttribute : PropertyAttribute
    {
        public Type ConstantsType { get; }

        /// <summary>
        /// No constants type specified — field draws as a normal string field.
        /// Used by the framework base class where the user's tag constants are unknown.
        /// </summary>
        public ActorTagAttribute() { }

        /// <summary>
        /// Specifies a type containing public const string fields to populate the searchable tag picker.
        /// </summary>
        public ActorTagAttribute(Type constantsType)
        {
            ConstantsType = constantsType;
        }
    }
}
