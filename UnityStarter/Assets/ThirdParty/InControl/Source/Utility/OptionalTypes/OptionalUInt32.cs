// ReSharper disable UnusedMember.Global
namespace InControl
{
	using System;
	using System.Globalization;
	using UnityEngine;


	[Serializable]
	public struct OptionalUInt32
	{
		[SerializeField]
		// ReSharper disable once InconsistentNaming
		bool hasValue;

		[SerializeField]
		// ReSharper disable once InconsistentNaming
		UInt32 value;


		public OptionalUInt32( UInt32 value )
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


		public UInt32 Value
		{
			get
			{
				if (!hasValue)
				{
					throw new OptionalTypeHasNoValueException( "Trying to get a value from an OptionalUInt32 that has no value." );
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


		public UInt32 GetValueOrDefault( UInt32 defaultValue )
		{
			return hasValue ? value : defaultValue;
		}


		public UInt32 GetValueOrZero()
		{
			// ReSharper disable once RedundantCast
			return hasValue ? value : (UInt32) 0;
		}


		public void SetValue( UInt32 value )
		{
			this.value = value;
			hasValue = true;
		}


		public override bool Equals( object other )
		{
			return ReferenceEquals( other, null ) && !hasValue ||
			       value.Equals( other );
		}


		public bool Equals( OptionalUInt32 other )
		{
			return hasValue &&
			       other.hasValue &&
			       value == other.value;
		}


		public bool Equals( UInt32 other )
		{
			return hasValue &&
			       value == other;
		}


		public static bool operator ==( OptionalUInt32 a, OptionalUInt32 b )
		{
			return a.hasValue &&
			       b.hasValue &&
			       a.value == b.value;
		}


		public static bool operator !=( OptionalUInt32 a, OptionalUInt32 b )
		{
			return !(a == b);
		}


		public static bool operator ==( OptionalUInt32 a, UInt32 b )
		{
			return a.hasValue && a.value == b;
		}


		public static bool operator !=( OptionalUInt32 a, UInt32 b )
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
			return hasValue ? value.ToString( CultureInfo.InvariantCulture ) : "";
		}


		public static implicit operator OptionalUInt32( UInt32 value )
		{
			return new OptionalUInt32( value );
		}


		public static explicit operator UInt32( OptionalUInt32 optional )
		{
			return optional.Value;
		}
	}
}
