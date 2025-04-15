// ReSharper disable UnusedMember.Global
namespace InControl
{
	using System;
	using System.Globalization;
	using UnityEngine;


	[Serializable]
	public struct OptionalInputDeviceDriverType
	{
		[SerializeField]
		// ReSharper disable once InconsistentNaming
		bool hasValue;

		[SerializeField]
		// ReSharper disable once InconsistentNaming
		InputDeviceDriverType value;


		public OptionalInputDeviceDriverType( InputDeviceDriverType value )
		{
			this.value = value;
			hasValue = true;
		}


		public bool HasValue
		{
			get
			{
				return hasValue;
			}
		}


		public bool HasNoValue
		{
			get
			{
				return !hasValue;
			}
		}


		public InputDeviceDriverType Value
		{
			get
			{
				if (!hasValue)
				{
					throw new OptionalTypeHasNoValueException( "Trying to get a value from an OptionalInputDeviceDriverType that has no value." );
				}

				return value;
			}

			set
			{
				this.value = value;
				hasValue = true;
			}
		}


		public void Clear()
		{
			value = 0;
			hasValue = false;
		}


		public InputDeviceDriverType GetValueOrDefault( InputDeviceDriverType defaultValue )
		{
			return hasValue ? value : defaultValue;
		}


		public InputDeviceDriverType GetValueOrZero()
		{
			// ReSharper disable once RedundantCast
			return hasValue ? value : (InputDeviceDriverType) 0;
		}


		public void SetValue( InputDeviceDriverType value )
		{
			this.value = value;
			hasValue = true;
		}


		public override bool Equals( object other )
		{
			return ReferenceEquals( other, null ) && !hasValue ||
			       value.Equals( other );
		}


		public bool Equals( OptionalInputDeviceDriverType other )
		{
			return hasValue &&
			       other.hasValue &&
			       value == other.value;
		}


		public bool Equals( InputDeviceDriverType other )
		{
			return hasValue &&
			       value == other;
		}


		public static bool operator ==( OptionalInputDeviceDriverType a, OptionalInputDeviceDriverType b )
		{
			return a.hasValue &&
			       b.hasValue &&
			       a.value == b.value;
		}


		public static bool operator !=( OptionalInputDeviceDriverType a, OptionalInputDeviceDriverType b )
		{
			return !(a == b);
		}


		public static bool operator ==( OptionalInputDeviceDriverType a, InputDeviceDriverType b )
		{
			return a.hasValue && a.value == b;
		}


		public static bool operator !=( OptionalInputDeviceDriverType a, InputDeviceDriverType b )
		{
			return !(a.hasValue && a.value == b);
		}


		static int CombineHashCodes( int h1, int h2 )
		{
			unchecked
			{
				return ((h1 << 5) + h1) ^ h2;
			}
		}


		public override int GetHashCode()
		{
			// ReSharper disable NonReadonlyMemberInGetHashCode
			return CombineHashCodes( hasValue.GetHashCode(), value.GetHashCode() );
			// ReSharper restore NonReadonlyMemberInGetHashCode
		}


		public override string ToString()
		{
			return hasValue ? value.ToString() : "";
		}


		public static implicit operator OptionalInputDeviceDriverType( InputDeviceDriverType value )
		{
			return new OptionalInputDeviceDriverType( value );
		}


		public static explicit operator InputDeviceDriverType( OptionalInputDeviceDriverType optional )
		{
			return optional.Value;
		}
	}
}
