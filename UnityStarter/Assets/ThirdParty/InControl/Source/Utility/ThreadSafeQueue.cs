namespace InControl
{
	using System.Collections.Generic;


	internal class ThreadSafeQueue<T>
	{
		object sync;
		Queue<T> data;


		public ThreadSafeQueue()
		{
			sync = new object();
			data = new Queue<T>();
		}


		public ThreadSafeQueue( int capacity )
		{
			sync = new object();
			data = new Queue<T>( capacity );
		}


		public void Enqueue( T item )
		{
			lock (sync)
			{
				data.Enqueue( item );
			}
		}


		public bool Dequeue( out T item )
		{
			lock (sync)
			{
				if (data.Count > 0)
				{
					item = data.Dequeue();
					return true;
				}
			}

			item = default(T);
			return false;
		}


		public T Dequeue()
		{
			lock (sync)
			{
				if (data.Count > 0)
				{
					return data.Dequeue();
				}
			}

			return default(T);
		}


		public int Dequeue( ref IList<T> list )
		{
			lock (sync)
			{
				var count = data.Count;
				for (var i = 0; i < count; i++)
				{
					list.Add( data.Dequeue() );
				}

				return count;
			}
		}
	}
}
