using UnityEditor;
using UnityEngine;

namespace InControl
{
	[CustomPropertyDrawer( typeof(InputControlSource) )]
	public class InputControlSourcePropertyDrawer : PropertyDrawer
	{
		public override void OnGUI( Rect position, SerializedProperty property, GUIContent label )
		{
			EditorGUI.BeginProperty( position, label, property );

			position = EditorGUI.PrefixLabel( position, GUIUtility.GetControlID( FocusType.Passive ), label );

			var indent = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;

			var halfWidth = (position.width / 2) - 3;
			var sourceTypeRect = new Rect( position.x, position.y, halfWidth, position.height );
			var indexRect = new Rect( position.x + halfWidth + 6, position.y, halfWidth, position.height );

			var sourceTypeProperty = property.FindPropertyRelative( "sourceType" );
			EditorGUI.PropertyField( sourceTypeRect, sourceTypeProperty, GUIContent.none );

			var indexProperty = property.FindPropertyRelative( "index" );

			var sourceType = (InputControlSourceType) sourceTypeProperty.enumValueIndex;
			switch (sourceType)
			{
				case InputControlSourceType.None:
					break;
				case InputControlSourceType.KeyCode:
					indexProperty.intValue = (int) (KeyCode) EditorGUI.EnumPopup( indexRect, (KeyCode) indexProperty.intValue );
					break;
				case InputControlSourceType.Button:
				case InputControlSourceType.Analog:
				default:
					EditorGUI.PropertyField( indexRect, indexProperty, GUIContent.none );
					break;
			}

			EditorGUI.indentLevel = indent;

			EditorGUI.EndProperty();
		}
	}
}
