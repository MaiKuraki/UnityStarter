namespace InControl
{
	public class KeyBindingSourceListener : BindingSourceListener
	{
		KeyCombo detectFound;
		int detectPhase;


		public void Reset()
		{
			detectFound.Clear();
			detectPhase = 0; // Wait for release.
		}


		public BindingSource Listen( BindingListenOptions listenOptions, InputDevice device )
		{
			if (!listenOptions.IncludeKeys)
			{
				return null;
			}

			if (detectFound.IncludeCount > 0)
			{
				if (!detectFound.IsPressed)
				{
					if (detectPhase == 2)
					{
						var bindingSource = new KeyBindingSource( detectFound );
						Reset();
						return bindingSource;
					}
				}
			}

			var keyCombo = KeyCombo.Detect( listenOptions.IncludeModifiersAsFirstClassKeys );
			if (keyCombo.IncludeCount > 0)
			{
				if (detectPhase == 1)
				{
					detectFound = keyCombo;
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
	}
}
