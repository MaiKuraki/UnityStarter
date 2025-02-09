#if UNITY_EDITOR
namespace InControl
{
	using UnityEditor;


	[CustomEditor( typeof(TouchStickControl) )]
	public class TouchStickControlEditor : TouchControlEditor
	{
		void OnEnable()
		{
			headerTexture = Internal.EditorTextures.TouchStickHeader;
		}
	}
}
#endif
