using System;

using UnityEngine;

namespace CycloneGames.InputSystem.Editor
{
    /// <summary>
    /// Package-local authoring helper. Draws a serialized string from the public
    /// <c>const string</c> fields declared by a type.
    /// </summary>
    /// <remarks>
    /// The type is local to this assembly so the Editor assembly has no dependency on
    /// CycloneGames.Utility. The serialized value is a plain string; the owning contract
    /// supplies validation rules.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class StringAsConstSelectorAttribute : PropertyAttribute
    {
        /// <summary>
        /// Gets the type whose public static constant string fields provide the known values.
        /// </summary>
        public Type ConstantsType { get; }

        /// <summary>
        /// Gets or sets whether known values are displayed in a hierarchical menu.
        /// </summary>
        public bool UseMenu { get; set; }

        /// <summary>
        /// Gets or sets whether values outside the known constants remain editable.
        /// </summary>
        public bool AllowCustom { get; set; }

        /// <summary>
        /// Gets or sets the field-name separator used to construct hierarchical menu paths.
        /// </summary>
        public char Separator { get; set; } = '_';

        public StringAsConstSelectorAttribute(Type constantsType)
        {
            ConstantsType = constantsType ?? throw new ArgumentNullException(nameof(constantsType));
        }
    }
}
