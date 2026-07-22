using System;
using System.Threading;

namespace CycloneGames.Persistence
{
    internal static class PersistenceExceptionPolicy
    {
        internal static bool IsRecoverable(Exception exception)
        {
            return !(exception is OutOfMemoryException)
                && !(exception is StackOverflowException)
                && !(exception is AccessViolationException)
                && !(exception is AppDomainUnloadedException)
                && !(exception is CannotUnloadAppDomainException)
                && !(exception is ThreadAbortException)
                && !(exception is BadImageFormatException)
                && !(exception is InvalidProgramException);
        }
    }
}
