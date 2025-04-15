namespace InControl
{
	public class MouseBindingSourceListener : BindingSourceListener
	{
		public static float ScrollWheelThreshold = 0.001f;

		Mouse detectFound;
		int detectPhase;


		public void Reset()
		{
			detectFound = Mouse.None;
			detectPhase = 0; // Wait for release.
		}


		public BindingSource Listen( BindingListenOptions listenOptions, InputDevice device )
		{
			if (detectFound != Mouse.None)
			{
				if (!IsPressed( detectFound ))
				{
					if (detectPhase == 2)
					{
						var bindingSource = new MouseBindingSource( detectFound );
						Reset();
						return bindingSource;
					}
				}
			}

			var control = ListenForControl( listenOptions );
			if (control != Mouse.None)
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


		bool IsPressed( Mouse control )
		{
			switch (control)
			{
				case Mouse.NegativeScrollWheel:
					return MouseBindingSource.NegativeScrollWheelIsActive( ScrollWheelThreshold );
				case Mouse.PositiveScrollWheel:
					return MouseBindingSource.PositiveScrollWheelIsActive( ScrollWheelThreshold );
				default:
					return MouseBindingSource.ButtonIsPressed( control );
			}
		}


		Mouse ListenForControl( BindingListenOptions listenOptions )
		{
			if (listenOptions.IncludeMouseButtons)
			{
				for (var control = Mouse.None; control <= Mouse.Button7; control++)
				{
					if (MouseBindingSource.ButtonIsPressed( control ))
					{
						return control;
					}
				}
			}

			if (listenOptions.IncludeMouseScrollWheel)
			{
				if (MouseBindingSource.NegativeScrollWheelIsActive( ScrollWheelThreshold ))
				{
					return Mouse.NegativeScrollWheel;
				}

				if (MouseBindingSource.PositiveScrollWheelIsActive( ScrollWheelThreshold ))
				{
					return Mouse.PositiveScrollWheel;
				}
			}

			return Mouse.None;
		}
	}
}
