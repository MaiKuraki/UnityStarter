using System;
using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
	/// <summary>
	/// original code: https://github.com/RodrigoPrinheiro/unityFoldoutAttribute
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
	public class PropertyGroupAttribute : PropertyAttribute
	{
		public string GroupName;
		public bool GroupAllFieldsUntilNextGroupAttribute;
		public int GroupColorIndex;
		public bool ClosedByDefault;

		public PropertyGroupAttribute(string groupName, bool groupAllFieldsUntilNextGroupAttribute = false, int groupColorIndex = 24, bool closedByDefault = false)
		{
			if (groupColorIndex > 139) { groupColorIndex = 139; }

			this.GroupName = groupName;
			this.GroupAllFieldsUntilNextGroupAttribute = groupAllFieldsUntilNextGroupAttribute;
			this.GroupColorIndex = groupColorIndex;
			this.ClosedByDefault = closedByDefault;
		}
	}
}