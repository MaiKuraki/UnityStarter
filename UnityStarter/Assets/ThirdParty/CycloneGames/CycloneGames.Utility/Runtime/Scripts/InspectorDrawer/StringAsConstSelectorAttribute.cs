
using System;
using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Displays a string property as a popup dropdown, populated with the values of public constant strings from a specified type.
    /// This is highly performant and produces no garbage during UI rendering after the initial cache is built.
    /// </summary>
    /// <example>
    /// <code>
    /// public static class GameConstants
    /// {
    ///     public const string PlayerTag = "Player";
    ///     public const string EnemyTag = "Enemy";
    /// }
    ///
    /// public class MyComponent : MonoBehaviour
    /// {
    ///     [StringAsConstSelector(typeof(GameConstants))]
    ///     public string TargetTag;
    /// }
    /// </code>
    /// </example>
    public class StringAsConstSelectorAttribute : PropertyAttribute
    {
        public Type ConstantsType { get; }

        /// <summary>
        /// Initializes a new instance of the StringAsConstSelectorAttribute.
        /// </summary>
        /// <param name="constantsType">The type containing the public const string fields to display.</param>
        public StringAsConstSelectorAttribute(Type constantsType)
        {
            ConstantsType = constantsType;
        }
    }
}