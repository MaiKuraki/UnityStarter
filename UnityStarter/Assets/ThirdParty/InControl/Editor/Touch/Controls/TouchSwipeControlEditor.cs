#if UNITY_EDITOR
namespace InControl
{
	using UnityEditor;


	[CustomEditor( typeof(TouchSwipeControl) )]
	public class TouchSwipeControlEditor : TouchControlEditor
	{
		void OnEnable()
		{
			headerTexture = Internal.EditorTextures.TouchSwipeHeader;
		}
	}
}
#endif
