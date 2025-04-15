namespace InControl
{
	public class DeviceBindingSourceListener : BindingSourceListener
	{
		InputControlType detectFound;
		int detectPhase;


		public void Reset()
		{
			detectFound = InputControlType.None;
			detectPhase = 0; // Wait for release.
		}


		public BindingSource Listen( BindingListenOptions listenOptions, InputDevice device )
		{
			if (!listenOptions.IncludeControllers || device.IsUnknown)
			{
				return null;
			}

			if (detectFound != InputControlType.None)
			{
				if (!IsPressed( detectFound, device ))
				{
					if (detectPhase == 2)
					{
						var bindingSource = new DeviceBindingSource( detectFound );
						Reset();
						return bindingSource;
					}
				}
			}

			var control = ListenForControl( listenOptions, device );
			if (control != InputControlType.None)
			{
				if (detectPhase == 1)
				{
					detectFound = control;
					detectPhase = 2; // Wait for release.
				}
			}
			else
			{
				if (detectPhase == 0)
				{
					detectPhase = 1; // Wait for press.
				}
			}

			return null;
		}


		bool IsPressed( InputControl control )
		{
			return Utility.AbsoluteIsOverThreshold( control.Value, 0.5f );
		}


		bool IsPressed( InputControlType control, InputDevice device )
		{
			return IsPressed( device.GetControl( control ) );
		}


		InputControlType ListenForControl( BindingListenOptions listenOptions, InputDevice device )
		{
			if (device.IsKnown)
			{
				var controlCount = device.Controls.Count;
				for (var i = 0; i < controlCount; i++)
				{
					var control = device.Controls[i];
					if (control != null && IsPressed( control ))
					{
						if (listenOptions.IncludeNonStandardControls || control.IsStandard)
						{
							var target = control.Target;
							if (target == InputControlType.Command && listenOptions.IncludeNonStandardControls)
							{
								continue;
							}

							return target;
						}
					}
				}
			}

			return InputControlType.None;
		}
	}
}
