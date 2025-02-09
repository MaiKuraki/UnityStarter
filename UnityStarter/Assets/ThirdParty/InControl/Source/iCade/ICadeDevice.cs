#if UNITY_IOS || UNITY_TVOS || UNITY_EDITOR
namespace InControl
{
	public class ICadeDevice : InputDevice
	{
		readonly ICadeDeviceManager owner;
		ICadeState state;


		public ICadeDevice( ICadeDeviceManager owner )
			: base( "iCade Controller" )
		{
			this.owner = owner;

			Meta = "iCade Controller on iOS";

			AddControl( InputControlType.DPadUp, "DPad Up" );
			AddControl( InputControlType.DPadDown, "DPad Down" );
			AddControl( InputControlType.DPadLeft, "DPad Left" );
			AddControl( InputControlType.DPadRight, "DPad Right" );

			AddControl( InputControlType.Action1, "Button 1" );
			AddControl( InputControlType.Action2, "Button 2" );
			AddControl( InputControlType.Action3, "Button 3" );
			AddControl( InputControlType.Action4, "Button 4" );
			AddControl( InputControlType.Action5, "Button 5" );
			AddControl( InputControlType.Action6, "Button 6" );
			AddControl( InputControlType.Action7, "Button 7" );
			AddControl( InputControlType.Action8, "Button 8" );

			// AddControl( InputControlType.LeftTrigger, "Left Trigger" );
			// AddControl( InputControlType.RightTrigger, "Right Trigger" );
			// AddControl( InputControlType.Select, "Select" );
			// AddControl( InputControlType.Start, "Start" );
		}


		public override void Update( ulong updateTick, float deltaTime )
		{
			GetState();

			UpdateWithState( InputControlType.DPadUp, IsPressed( ICadeState.DPadUp ), updateTick, deltaTime );
			UpdateWithState( InputControlType.DPadDown, IsPressed( ICadeState.DPadDown ), updateTick, deltaTime );
			UpdateWithState( InputControlType.DPadLeft, IsPressed( ICadeState.DPadLeft ), updateTick, deltaTime );
			UpdateWithState( InputControlType.DPadRight, IsPressed( ICadeState.DPadRight ), updateTick, deltaTime );

			UpdateWithState( InputControlType.Action1, IsPressed( ICadeState.Button1 ), updateTick, deltaTime );
			UpdateWithState( InputControlType.Action2, IsPressed( ICadeState.Button2 ), updateTick, deltaTime );
			UpdateWithState( InputControlType.Action3, IsPressed( ICadeState.Button3 ), updateTick, deltaTime );
			UpdateWithState( InputControlType.Action4, IsPressed( ICadeState.Button4 ), updateTick, deltaTime );
			UpdateWithState( InputControlType.Action5, IsPressed( ICadeState.Button5 ), updateTick, deltaTime );
			UpdateWithState( InputControlType.Action6, IsPressed( ICadeState.Button6 ), updateTick, deltaTime );
			UpdateWithState( InputControlType.Action7, IsPressed( ICadeState.Button7 ), updateTick, deltaTime );
			UpdateWithState( InputControlType.Action8, IsPressed( ICadeState.Button8 ), updateTick, deltaTime );

			// UpdateWithState( InputControlType.RightTrigger, IsPressed( ICadeState.Button5 ), updateTick, deltaTime );
			// UpdateWithState( InputControlType.LeftTrigger, IsPressed( ICadeState.Button6 ), updateTick, deltaTime );
			// UpdateWithState( InputControlType.Start, IsPressed( ICadeState.Button1 ), updateTick, deltaTime );
			// UpdateWithState( InputControlType.Select, IsPressed( ICadeState.Button3 ), updateTick, deltaTime );
		}


		internal bool IsPressed( ICadeState flags )
		{
			return (state & flags) != 0;
		}


		internal void GetState()
		{
			state = owner.GetState();
		}
	}
}
#endif
