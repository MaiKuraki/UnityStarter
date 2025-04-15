#if UNITY_IOS || UNITY_TVOS || UNITY_EDITOR
namespace InControl
{
	using System;


	[Flags]
	public enum ICadeState
	{
		None = 0x000,
		DPadUp = 0x001,
		DPadRight = 0x002,
		DPadDown = 0x004,
		DPadLeft = 0x008,
		Button1 = 0x010,
		Button2 = 0x020,
		Button3 = 0x040,
		Button4 = 0x080,
		Button5 = 0x100,
		Button6 = 0x200,
		Button7 = 0x400,
		Button8 = 0x800,
	};
}
#endif
