// ReSharper disable UnusedMember.Global
namespace InControl
{
	using System;
	using System.Globalization;
	using UnityEngine;


	[Serializable]
	public struct OptionalInt16
	{
		[SerializeField]
		// ReSharper disable once InconsistentNaming
		bool hasValue;

		[SerializeField]
		// ReSharper disable once InconsistentNaming
		Int16 value;


		public OptionalInt16( Int16 value )
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


		public Int16 Value
		{
			get
			{
				if (!hasValue)
				{
					throw new OptionalTypeHasNoValueException( "Trying to get a value from an OptionalInt16 that has no value." );
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


		public Int16 GetValueOrDefault( Int16 defaultValue )
		{
			return hasValue ? value : defaultValue;
		}


		public Int16 GetValueOrZero()
		{
			return hasValue ? value : (Int16) 0;
		}


		public void SetValue( Int16 value )
		{
			this.value = value;
			hasValue = true;
		}


		public override bool Equals( object other )
		{
			return ReferenceEquals( other, null ) && !hasValue ||
			       value.Equals( other );
		}


		public bool Equals( OptionalInt16 other )
		{
			return hasValue &&
			       other.hasValue &&
			       value == other.value;
		}


		public bool Equals( Int16 other )
		{
			return hasValue &&
			       value == other;
		}


		public static bool operator ==( OptionalInt16 a, OptionalInt16 b )
		{
			return a.hasValue &&
			       b.hasValue &&
			       a.value == b.value;
		}


		public static bool operator !=( OptionalInt16 a, OptionalInt16 b )
		{
			return !(a == b);
		}


		public static bool operator ==( OptionalInt16 a, Int16 b )
		{
			return a.hasValue && a.value == b;
		}


		public static bool operator !=( OptionalInt16 a, Int16 b )
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


		public static implicit operator OptionalInt16( Int16 value )
		{
			return new OptionalInt16( value );
		}


		public static explicit operator Int16( OptionalInt16 optional )
		{
			return optional.Value;
		}
	}
}
