using System;

using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Draws a serialized string from the public <c>const string</c> fields declared by a type.
    /// </summary>
    /// <remarks>
    /// The attribute changes Editor authoring only. The serialized value remains a plain string and
    /// therefore requires the owning contract to define its own validation and migration rules.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StringAsConstSelectorAttribute : PropertyAttribute
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
