namespace InControl
{
	using System;
	using System.IO;


	public class KeyBindingSource : BindingSource
	{
		public KeyCombo Control { get; protected set; }


		internal KeyBindingSource() {}


		public KeyBindingSource( KeyCombo keyCombo )
		{
			Control = keyCombo;
		}


		public KeyBindingSource( params Key[] keys )
		{
			Control = new KeyCombo( keys );
		}


		public override float GetValue( InputDevice inputDevice )
		{
			return GetState( inputDevice ) ? 1.0f : 0.0f;
		}


		public override bool GetState( InputDevice inputDevice )
		{
			return Control.IsPressed;
		}


		public override string Name
		{
			get
			{
				return Control.ToString();
			}
		}


		public override string DeviceName
		{
			get
			{
				return "Keyboard";
			}
		}


		public override InputDeviceClass DeviceClass
		{
			get
			{
				return InputDeviceClass.Keyboard;
			}
		}


		public override InputDeviceStyle DeviceStyle
		{
			get
			{
				return InputDeviceStyle.Unknown;
			}
		}


		public override bool Equals( BindingSource other )
		{
			if (other == null)
			{
				return false;
			}

			var bindingSource = other as KeyBindingSource;
			if (bindingSource != null)
			{
				return Control == bindingSource.Control;
			}

			return false;
		}


		public override bool Equals( object other )
		{
			if (other == null)
			{
				return false;
			}

			var bindingSource = other as KeyBindingSource;
			if (bindingSource != null)
			{
				return Control == bindingSource.Control;
			}

			return false;
		}


		public override int GetHashCode()
		{
			return Control.GetHashCode();
		}


		public override BindingSourceType BindingSourceType
		{
			get
			{
				return BindingSourceType.KeyBindingSource;
			}
		}


		public override void Load( BinaryReader reader, UInt16 dataFormatVersion )
		{
			// Have to do this because it's a struct property? Weird.
			var temp = new KeyCombo();
			temp.Load( reader, dataFormatVersion );
			Control = temp;
		}


		public override void Save( BinaryWriter writer )
		{
			Control.Save( writer );
		}
	}
}
