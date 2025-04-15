#if !UNITY_2019_2_OR_NEWER
namespace InControl
{
#if UNITY_EDITOR
	using UnityEditor;
#endif
	using System;
	using UnityEngine;


	[AttributeUsage( AttributeTargets.Field, Inherited = true, AllowMultiple = false )]
	public class InspectorNameAttribute : PropertyAttribute
	{
		public readonly string displayName;

		public InspectorNameAttribute( string displayName )
		{
			this.displayName = displayName;
		}
	}
}
#endif
