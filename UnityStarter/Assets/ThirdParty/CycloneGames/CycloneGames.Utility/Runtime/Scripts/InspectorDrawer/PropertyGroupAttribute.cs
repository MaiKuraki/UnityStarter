using System;
using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
	/// <summary>
	/// An attribute to group fields in the inspector under a foldout.
	/// original code: https://github.com/RodrigoPrinheiro/unityFoldoutAttribute
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
	public class PropertyGroupAttribute : PropertyAttribute
	{
		public string GroupName;
		public bool GroupAllFieldsUntilNextGroupAttribute;
		public int GroupColorIndex;
		public bool ClosedByDefault;

		/// <summary>
		/// Groups fields in the inspector.
		/// </summary>
		/// <param name="groupName">The name of the group.</param>
		/// <param name="groupAllFieldsUntilNextGroupAttribute">If true, all fields until the next group attribute will be part of this group.</param>
		/// <param name="groupColorIndex">Index of the color for the group's side bar (0-139).</param>
		/// <param name="closedByDefault">Whether the foldout is closed by default.</param>
		public PropertyGroupAttribute(string groupName, bool groupAllFieldsUntilNextGroupAttribute = false, int groupColorIndex = 24, bool closedByDefault = false)
		{
			// Clamp the color index to be within the valid range.
			if (groupColorIndex > 139) { groupColorIndex = 139; }
			if (groupColorIndex < 0) { groupColorIndex = 0; }

			this.GroupName = groupName;
			this.GroupAllFieldsUntilNextGroupAttribute = groupAllFieldsUntilNextGroupAttribute;
			this.GroupColorIndex = groupColorIndex;
			this.ClosedByDefault = closedByDefault;
		}
	}
}