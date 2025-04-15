#if UNITY_EDITOR
namespace InControl
{
	using UnityEditor;


	[CustomEditor( typeof(TouchButtonControl) )]
	public class TouchButtonControlEditor : TouchControlEditor
	{
		void OnEnable()
		{
			headerTexture = Internal.EditorTextures.TouchButtonHeader;
		}
	}
}
#endif
