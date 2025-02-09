#if UNITY_EDITOR
namespace InControl
{
	using UnityEditor;


	[CustomEditor( typeof(TouchTrackControl) )]
	public class TouchTrackControlEditor : TouchControlEditor
	{
		void OnEnable()
		{
			headerTexture = Internal.EditorTextures.TouchTrackHeader;
		}
	}
}
#endif
