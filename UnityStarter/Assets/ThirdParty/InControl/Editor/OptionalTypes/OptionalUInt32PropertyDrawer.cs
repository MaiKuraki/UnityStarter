using System;
using UnityEditor;
using UnityEngine;

namespace InControl
{
	using Internal;


	[CustomPropertyDrawer( typeof(OptionalUInt32) )]
	class OptionalUInt32PropertyDrawer : PropertyDrawer
	{
		static ulong ParseNumber( string text )
		{
			text = text.Trim();

			if (text.StartsWith( "0x", StringComparison.OrdinalIgnoreCase ))
			{
				text = text.Substring( 2 );
				try
				{
					return ulong.Parse( text, System.Globalization.NumberStyles.HexNumber );
				}
				catch
				{
					// Ignore
				}
			}

			try
			{
				return ulong.Parse( text );
			}
			catch
			{
				return 0;
			}
		}


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
				if (fieldInfo.GetCustomAttributes( typeof(HexadecimalAttribute), true ).Length > 0)
				{
					var textValue = EditorGUI.TextField( valueRect, GUIContent.none, string.Format( "0x{0:x8}", serializedProperty.longValue ) ).Trim();
					var longValue = ParseNumber( textValue );
					serializedProperty.longValue = longValue >= UInt32.MaxValue ? UInt32.MaxValue : (UInt32) longValue;
				}
				else
				{
					EditorGUI.PropertyField( valueRect, serializedProperty, GUIContent.none );
				}
			}
			else
			{
				GUI.Label( valueRect, "Empty (Optional UInt32)", "TextField" );
			}

			EditorGUI.EndDisabledGroup();

			EditorGUI.indentLevel = indent;

			EditorGUI.EndProperty();
		}
	}
}
