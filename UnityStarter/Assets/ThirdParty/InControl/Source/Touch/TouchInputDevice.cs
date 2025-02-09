namespace InControl
{
	public class TouchInputDevice : InputDevice
	{
		public TouchInputDevice()
			: base( "Touch Input Device", true )
		{
			DeviceClass = InputDeviceClass.TouchScreen;
		}
	}
}
