
using System;
using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Displays a string property as a popup dropdown, populated with the values of public constant strings from a specified type.
    /// This is highly performant and produces no garbage during UI rendering after the initial cache is built.
    /// Note: If you want to use in List<string>, its dangerous, you may change the string variable name or variable value, but it will give the list a wrong value.
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
        /// When true, displays the options in a hierarchical GenericMenu instead of a flat popup.
        /// </summary>
        public bool UseMenu { get; set; } = false;

        /// <summary>
        /// The character used to separate path segments in the constant's field name for the hierarchical menu.
        /// </summary>
        public char Separator { get; set; } = '_';

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