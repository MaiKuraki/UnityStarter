using UnityEditor;
using UnityEngine;

namespace InControl
{
	using Internal;


	[CustomPropertyDrawer( typeof(OptionalInt16) )]
	class OptionalInt16PropertyDrawer : PropertyDrawer
	{
		public override void OnGUI( Rect position, SerializedProperty property, GUIContent label )
		{
			EditorGUI.BeginProperty( position, label, property );

			var serializedProperty = property.Copy();

			serializedProperty.NextVisible( true );

			var fullWidth = position.width;
			position.width = EditorGUIUtility.labelWidth - 10;
			var checkValue = EditorGUI.ToggleLeft( position, label, serializedProperty.boolValue, EditorUtility.labelStyle );
			serializedProperty.boolValue = checkValue;
			position.width = fullWidth;

			var indent = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;

			serializedProperty.NextVisible( true );
			var valueRect = new Rect(
				position.x + EditorGUIUtility.labelWidth,
				position.y,
				position.width - EditorGUIUtility.labelWidth,
				position.height
			);

			EditorGUI.BeginDisabledGroup( !checkValue );

			if (checkValue)
			{
				EditorGUI.PropertyField( valueRect, serializedProperty, GUIContent.none );
			}
			else
			{
				GUI.Label( valueRect, "Empty (Optional Int16)", "TextField" );
			}

			EditorGUI.EndDisabledGroup();

			EditorGUI.indentLevel = indent;

			EditorGUI.EndProperty();
		}
	}
}
