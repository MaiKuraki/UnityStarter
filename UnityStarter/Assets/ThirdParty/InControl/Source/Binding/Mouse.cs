using System;

namespace InControl
{
	public enum Mouse : int
	{
		None,
		LeftButton,
		RightButton,
		MiddleButton,
		NegativeX,
		PositiveX,
		NegativeY,
		PositiveY,
		PositiveScrollWheel,
		NegativeScrollWheel,
		Button4,
		Button5,
		Button6,
		Button7,

		[Obsolete( "Mouse.Button8 is no longer supported and will be removed in a future version." )]
		Button8,

		[Obsolete( "Mouse.Button9 is no longer supported and will be removed in a future version." )]
		Button9
	}
}
