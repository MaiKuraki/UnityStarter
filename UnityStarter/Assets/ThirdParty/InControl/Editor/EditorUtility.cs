#if UNITY_EDITOR
namespace InControl.Internal
{
	using UnityEditor;
	using UnityEngine;


	internal static class EditorUtility
	{
		internal static GUIStyle titleStyle;
		internal static GUIStyle groupStyle;
		internal static GUIStyle labelStyle;
		internal static GUIStyle wellStyle;

		static Color defaultBackgroundColor;


		static EditorUtility()
		{
			defaultBackgroundColor = GUI.backgroundColor;

			titleStyle = new GUIStyle();
			titleStyle.border = new RectOffset( 2, 2, 2, 1 );
			titleStyle.margin = new RectOffset( 5, 5, 5, 0 );
			titleStyle.padding = new RectOffset( 5, 5, 5, 5 );
			titleStyle.alignment = TextAnchor.MiddleLeft;
			titleStyle.normal.background = IsProSkin ? Internal.EditorTextures.InspectorTitle_Pro : Internal.EditorTextures.InspectorTitle;
			titleStyle.normal.textColor = IsProSkin ? ProTextColor : TextColor;

			groupStyle = new GUIStyle();
			groupStyle.border = new RectOffset( 2, 2, 1, 2 );
			groupStyle.margin = new RectOffset( 5, 5, 5, 5 );
			groupStyle.padding = new RectOffset( 10, 10, 10, 10 );
			groupStyle.normal.background = IsProSkin ? Internal.EditorTextures.InspectorGroup_Pro : Internal.EditorTextures.InspectorGroup;
			groupStyle.normal.textColor = IsProSkin ? ProTextColor : TextColor;

			labelStyle = new GUIStyle();
			labelStyle.richText = true;
			labelStyle.padding.top = 1;
			labelStyle.padding.left = 5;
			labelStyle.normal.textColor = IsProSkin ? ProTextColor : TextColor;

			wellStyle = new GUIStyle();
			wellStyle.alignment = TextAnchor.UpperLeft;
			wellStyle.border = new RectOffset( 2, 2, 2, 2 );
			wellStyle.margin = new RectOffset( 5, 5, 5, 5 );
			wellStyle.padding = new RectOffset( 10, 10, 5, 7 );
			wellStyle.wordWrap = true;
			wellStyle.normal.background = IsProSkin ? Internal.EditorTextures.InspectorWell_Pro : Internal.EditorTextures.InspectorWell;
			wellStyle.normal.textColor = IsProSkin ? ProTextColor : TextColor;
			wellStyle.richText = true;
		}


		internal static Color TextColor
		{
			get
			{
				return new Color( 0.0f, 0.0f, 0.0f );
			}
		}


		internal static Color ProTextColor
		{
			get
			{
				return new Color( 0.8f, 0.8f, 0.8f );
			}
		}


		internal static bool IsProSkin
		{
			get
			{
				return EditorGUIUtility.isProSkin;
			}
		}


		static Color TintColor
		{
			get
			{
				return Color.white;
			}
		}


		internal static void SetTintColor()
		{
			GUI.backgroundColor = TintColor;
		}


		internal static void PopTintColor()
		{
			GUI.backgroundColor = defaultBackgroundColor;
		}


		internal static void GroupTitle( string title, SerializedProperty boolProperty )
		{
			SetTintColor();
			GUILayout.Space( 4.0f );
			GUILayout.BeginVertical( "", titleStyle );
			boolProperty.boolValue = EditorGUILayout.ToggleLeft( "<b>" + title + "</b>", boolProperty.boolValue, labelStyle );
			GUILayout.EndVertical();
			PopTintColor();
		}


		internal static void BeginGroup()
		{
			SetTintColor();
			GUILayout.Space( -6.0f );
			GUILayout.BeginVertical( "", groupStyle );
			PopTintColor();
		}


		internal static void BeginGroup( string title )
		{
			SetTintColor();
			GUILayout.Space( 4.0f );
			GUILayout.BeginVertical( "", titleStyle );
			EditorGUILayout.LabelField( "<b>" + title + "</b>", labelStyle );
			GUILayout.EndVertical();
			BeginGroup();
		}


		internal static void EndGroup()
		{
			GUILayout.EndVertical();
		}
	}
}
#endif
