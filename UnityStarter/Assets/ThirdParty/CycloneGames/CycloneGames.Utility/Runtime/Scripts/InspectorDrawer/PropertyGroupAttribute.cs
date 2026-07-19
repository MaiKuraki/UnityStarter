using System;

using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Declares an Inspector foldout group for a serialized field.
    /// </summary>
    /// <remarks>
    /// Rendering is opt-in through a target-specific Editor derived from
    /// <c>PropertyGroupInspectorDrawer</c>. The broad attribute target list is retained for source
    /// compatibility; the renderer only interprets attributes attached to visible serialized fields.
    /// </remarks>
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct,
        Inherited = true)]
    public class PropertyGroupAttribute : PropertyAttribute
    {
        public string GroupName;
        public bool GroupAllFieldsUntilNextGroupAttribute;
        public int GroupColorIndex;
        public bool ClosedByDefault;

        /// <summary>
        /// Creates a serialized-field group declaration.
        /// </summary>
        /// <param name="groupName">The foldout title.</param>
        /// <param name="groupAllFieldsUntilNextGroupAttribute">
        /// Whether following visible serialized fields remain in this group until another group,
        /// an <see cref="EndPropertyGroupAttribute"/>, or the end of the Inspector.
        /// </param>
        /// <param name="groupColorIndex">Legacy Utility palette index in the inclusive range 0-139.</param>
        /// <param name="closedByDefault">Whether a newly created Inspector starts with the group collapsed.</param>
        public PropertyGroupAttribute(
            string groupName,
            bool groupAllFieldsUntilNextGroupAttribute = false,
            int groupColorIndex = 24,
            bool closedByDefault = false)
        {
            if (groupColorIndex > 139)
            {
                groupColorIndex = 139;
            }
            else if (groupColorIndex < 0)
            {
                groupColorIndex = 0;
            }

            GroupName = groupName;
            GroupAllFieldsUntilNextGroupAttribute = groupAllFieldsUntilNextGroupAttribute;
            GroupColorIndex = groupColorIndex;
            ClosedByDefault = closedByDefault;
        }
    }
}
