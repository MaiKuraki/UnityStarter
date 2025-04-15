namespace InControl
{
	public class UnknownDeviceBindingSourceListener : BindingSourceListener
	{
		enum DetectPhase : int
		{
			WaitForInitialRelease = 0,
			WaitForControlPress,
			WaitForControlRelease
		}


		UnknownDeviceControl detectFound;
		DetectPhase detectPhase;


		public void Reset()
		{
			detectFound = UnknownDeviceControl.None;
			detectPhase = DetectPhase.WaitForInitialRelease;
			TakeSnapshotOnUnknownDevices();
		}


		void TakeSnapshotOnUnknownDevices()
		{
			var deviceCount = InputManager.Devices.Count;
			for (var i = 0; i < deviceCount; i++)
			{
				var device = InputManager.Devices[i];
				if (device.IsUnknown)
				{
					device.TakeSnapshot();
				}
			}
		}


		public BindingSource Listen( BindingListenOptions listenOptions, InputDevice device )
		{
			if (!listenOptions.IncludeUnknownControllers || device.IsKnown)
			{
				return null;
			}

			if (detectPhase == DetectPhase.WaitForControlRelease && detectFound)
			{
				if (!IsPressed( detectFound, device ))
				{
					var bindingSource = new UnknownDeviceBindingSource( detectFound );
					Reset();
					return bindingSource;
				}
			}

			var control = ListenForControl( listenOptions, device );
			if (control)
			{
				if (detectPhase == DetectPhase.WaitForControlPress)
				{
					detectFound = control;
					detectPhase = DetectPhase.WaitForControlRelease;
				}
			}
			else
			{
				if (detectPhase == DetectPhase.WaitForInitialRelease)
				{
					detectPhase = DetectPhase.WaitForControlPress;
				}
			}

			return null;
		}


		bool IsPressed( UnknownDeviceControl control, InputDevice device )
		{
			var value = control.GetValue( device );
			return Utility.AbsoluteIsOverThreshold( value, 0.5f );
		}


		UnknownDeviceControl ListenForControl( BindingListenOptions listenOptions, InputDevice device )
		{
			if (device.IsUnknown)
			{
				var button = device.GetFirstPressedButton();
				if (button)
				{
					return button;
				}

				var analog = device.GetFirstPressedAnalog();
				if (analog)
				{
					return analog;
				}
			}

			return UnknownDeviceControl.None;
		}
	}
}
