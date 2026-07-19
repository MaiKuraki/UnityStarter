using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.Factory.Runtime
{
    internal sealed class ReferenceIdentityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceIdentityComparer<T> Instance = new ReferenceIdentityComparer<T>();

        private ReferenceIdentityComparer()
        {
        }

        public bool Equals(T x, T y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
