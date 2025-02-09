#if UNITY_4_3
namespace InControl
{
#if UNITY_EDITOR
	using UnityEditor;
#endif
	using UnityEngine;


	public class HeaderAttribute : PropertyAttribute
	{
		public string header;

		public HeaderAttribute( string header )
		{
			this.header = header;
		}
	}

#if UNITY_EDITOR
	[CustomPropertyDrawer( typeof( HeaderAttribute ) )]
	public class HeaderDrawer : PropertyDrawer
	{
		public override float GetPropertyHeight( SerializedProperty property, GUIContent label )
		{
			return EditorGUI.GetPropertyHeight( property, label ) + (EditorGUIUtility.singleLineHeight * 1.5f);
		}


		public override void OnGUI( Rect position, SerializedProperty property, GUIContent label )
		{
			var headerAttribute = attribute as HeaderAttribute;
			var propertyHeight = position.height - (EditorGUIUtility.singleLineHeight * 1.5f);

			position.y += EditorGUIUtility.singleLineHeight * 0.5f;
			position.height = EditorGUIUtility.singleLineHeight;
			EditorGUI.LabelField( position, headerAttribute.header, EditorStyles.boldLabel );

			position.y += EditorGUIUtility.singleLineHeight;
			position.height = propertyHeight;
			EditorGUI.PropertyField( position, property );
		}
	}
#endif
}
#endif
