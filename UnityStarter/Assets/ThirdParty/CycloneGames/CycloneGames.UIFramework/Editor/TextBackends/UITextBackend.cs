using System;
using System.Collections.Generic;

namespace CycloneGames.UIFramework.Editor
{
    /// <summary>
    /// Describes one text component technology to editor tooling that has to recognize text
    /// components without taking a compile-time dependency on the package that ships them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matching compares the reflected type name, not a loaded <see cref="Type"/>, so a backend can
    /// be described by an assembly that does not reference the package that ships it.
    /// </para>
    /// <para>
    /// Instances are immutable and safe to share across domain reloads.
    /// </para>
    /// </remarks>
    public sealed class UITextBackend
    {
        private readonly string[] _titleObjectNames;

        /// <param name="id">
        /// Stable identifier. Registering two backends with the same id replaces the earlier one, so
        /// repeated registration after a domain reload is idempotent.
        /// </param>
        /// <param name="displayName">Name shown in editor UI.</param>
        /// <param name="baseTypeName">
        /// Fully qualified name of the base type shared by every text component of this backend.
        /// Matching walks the inheritance chain, so derived components such as
        /// <c>TextMeshProUGUI</c> match a base of <c>TMPro.TMP_Text</c>.
        /// </param>
        /// <param name="textFieldName">
        /// Serialized field that holds the text, used with
        /// <c>SerializedObject.FindProperty</c>.
        /// </param>
        /// <param name="priority">
        /// Match priority. Higher values are consulted first. It is the only supported way to decide
        /// between two backends that match the same component type, because registration order
        /// across assemblies is not deterministic.
        /// </param>
        /// <param name="titleObjectNames">
        /// GameObject names that mark the preferred title object in a template prefab. May be empty.
        /// </param>
        public UITextBackend(
            string id,
            string displayName,
            string baseTypeName,
            string textFieldName,
            int priority = 0,
            params string[] titleObjectNames)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A text backend id is required.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(baseTypeName))
            {
                throw new ArgumentException("A text backend base type name is required.", nameof(baseTypeName));
            }

            if (string.IsNullOrWhiteSpace(textFieldName))
            {
                throw new ArgumentException("A text backend text field name is required.", nameof(textFieldName));
            }

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            BaseTypeName = baseTypeName;
            TextFieldName = textFieldName;
            Priority = priority;
            _titleObjectNames = titleObjectNames ?? Array.Empty<string>();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string BaseTypeName { get; }
        public string TextFieldName { get; }

        /// <summary>
        /// Match priority. Higher values are consulted before lower ones.
        /// </summary>
        public int Priority { get; }

        /// <summary>
        /// GameObject names that mark the preferred title object in a template prefab.
        /// </summary>
        public IReadOnlyList<string> TitleObjectNames => _titleObjectNames;

        /// <summary>
        /// Reports whether <paramref name="componentType"/> derives from this backend's base type.
        /// </summary>
        public bool Matches(Type componentType)
        {
            for (Type type = componentType; type != null; type = type.BaseType)
            {
                if (string.Equals(type.FullName, BaseTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString() => DisplayName;
    }
}
