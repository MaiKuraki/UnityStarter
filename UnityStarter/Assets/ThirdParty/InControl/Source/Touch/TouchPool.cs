namespace InControl
{
	using System.Collections.Generic;
	using System.Collections.ObjectModel;


	public class TouchPool
	{
		public readonly ReadOnlyCollection<Touch> Touches;

		List<Touch> usedTouches;
		List<Touch> freeTouches;


		public TouchPool( int capacity )
		{
			freeTouches = new List<Touch>( capacity );
			for (var i = 0; i < capacity; i++)
			{
				freeTouches.Add( new Touch() );
			}

			usedTouches = new List<Touch>( capacity );

			Touches = new ReadOnlyCollection<Touch>( usedTouches );
		}


		public TouchPool()
			: this( 16 ) {}


		public Touch FindOrCreateTouch( int fingerId )
		{
			Touch touch;

			var touchCount = usedTouches.Count;
			for (var i = 0; i < touchCount; i++)
			{
				touch = usedTouches[i];
				if (touch.fingerId == fingerId)
				{
					return touch;
				}
			}

			touch = NewTouch();
			touch.fingerId = fingerId;
			usedTouches.Add( touch );
			return touch;
		}


		public Touch FindTouch( int fingerId )
		{
			var touchCount = usedTouches.Count;
			for (var i = 0; i < touchCount; i++)
			{
				var touch = usedTouches[i];
				if (touch.fingerId == fingerId)
				{
					return touch;
				}
			}

			return null;
		}


		Touch NewTouch()
		{
			var touchCount = freeTouches.Count;
			if (touchCount > 0)
			{
				var touch = freeTouches[touchCount - 1];
				freeTouches.RemoveAt( touchCount - 1 );
				return touch;
			}

			return new Touch();
		}


		public void FreeTouch( Touch touch )
		{
			touch.Reset();
			freeTouches.Add( touch );
		}


		public void FreeEndedTouches()
		{
			var touchCount = usedTouches.Count;
			for (var i = touchCount - 1; i >= 0; i--)
			{
				var touch = usedTouches[i];
				if (touch.phase == UnityEngine.TouchPhase.Ended)
				{
					usedTouches.RemoveAt( i );
				}
			}
		}
	}
}
