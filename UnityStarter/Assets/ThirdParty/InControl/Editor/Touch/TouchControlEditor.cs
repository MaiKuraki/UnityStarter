#if UNITY_EDITOR
namespace InControl
{
	using UnityEditor;
	using UnityEngine;


	public class TouchControlEditor : Editor
	{
		protected Texture headerTexture;
		Rect headerTextureRect;


		protected void AddHeaderImageSpace()
		{
			if (headerTexture != null)
			{
				GUILayout.Space( 5 );

				headerTextureRect = GUILayoutUtility.GetRect( 0.0f, -22.0f );
				headerTextureRect.width = headerTexture.width / 2;
				headerTextureRect.height = headerTexture.height / 2;

				GUILayout.Space( headerTextureRect.height );
			}
		}


		protected void DrawHeaderImage()
		{
			if (headerTexture != null)
			{
				GUI.DrawTexture( headerTextureRect, headerTexture );
			}
		}


		public override void OnInspectorGUI()
		{
			AddHeaderImageSpace();

			if (DrawDefaultInspector())
			{
				if (Application.isPlaying)
				{
					foreach (var target in targets)
					{
						(target as TouchControl).SendMessage( "ConfigureControl" );
					}
				}
			}

			DrawHeaderImage();
		}
	}
}
#endif
