// using UnityEditor;
// using UnityEngine;
//
// namespace InControl
// {
// 	[CustomPropertyDrawer( typeof(InputControlMapping) )]
// 	public class InputControlMappingPropertyDrawer : PropertyDrawer
// 	{
// 		public override void OnGUI( Rect position, SerializedProperty property, GUIContent label )
// 		{
// 			EditorGUI.BeginProperty( position, label, property );
//
// 			// if (property.isExpanded)
// 			// {
// 			var iterProperty = property.Copy();
// 			var lastProperty = iterProperty.GetEndProperty();
// 			var enterChildren = true;
// 			while (iterProperty.NextVisible( enterChildren ) && !SerializedProperty.EqualContents( iterProperty, lastProperty ))
// 			{
// 				position.height = EditorGUI.GetPropertyHeight( iterProperty, null, false );
// 				enterChildren = EditorGUI.PropertyField( position, iterProperty, false ) && iterProperty.hasVisibleChildren;
// 				position.y += position.height + 2.0f;
// 			}
// 			// }
//
// 			EditorGUI.EndProperty();
// 		}
//
//
// 		public override float GetPropertyHeight( SerializedProperty property, GUIContent label )
// 		{
// 			var h = 0.0f;
//
// 			var iterProperty = property.Copy();
// 			var lastProperty = iterProperty.GetEndProperty();
// 			var enterChildren = true;
// 			while (iterProperty.NextVisible( enterChildren ) && !SerializedProperty.EqualContents( iterProperty, lastProperty ))
// 			{
// 				enterChildren = EditorGUI.PropertyField( Rect.zero, iterProperty, false ) && iterProperty.hasVisibleChildren;
// 				h += EditorGUI.GetPropertyHeight( iterProperty, null, false ) + 2.0f;
// 			}
//
// 			return h;
// 		}
// 	}
// }


