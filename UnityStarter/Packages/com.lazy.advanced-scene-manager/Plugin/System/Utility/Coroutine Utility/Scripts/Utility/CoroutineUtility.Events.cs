using System.Collections;

namespace AdvancedSceneManager.Utility
{

    public static partial class CoroutineUtility
    {

        /// <summary>Provides events for coroutine events.</summary>
        public static class Events
        {

            static bool m_enableEvents;
            /// <summary>Enables or disables events. Setter not available, and getter always returns false, in build. Default is <see langword="false"/>.</summary>
            public static bool enableEvents
            {
                get
                {
#if UNITY_EDITOR
                    return m_enableEvents;
#else
                    return false;
#endif
                }
#if UNITY_EDITOR
                set => m_enableEvents = value;
#endif
            }

            /// <param name="coroutine">The coroutine that this event was called for.</param>
            public delegate void CoroutineEvent(GlobalCoroutine coroutine);

            /// <param name="coroutine">The coroutine that this event was called for.</param>
            /// <param name="data">The object returned from <see cref="IEnumerator.Current"/>.</param>
            /// <param name="level">The level, or depth, of the current subroutine.</param>
            /// <param name="parentUserData">The userdata of the subroutine above this one, depth-wise.</param>
            /// <param name="isPause"><see cref="GlobalCoroutine.Pause"/> is reported as a subroutine, this is true when that is the case.</param>
            public delegate object CoroutineFrameStartEvent(GlobalCoroutine coroutine, object data, int level, object parentUserData, bool isPause);

            /// <param name="coroutine">The coroutine that this event was called for.</param>
            /// <param name="userData">The userdata that was passed to <see cref="onSubroutineStart"/>.</param>
            public delegate void CoroutineFrameEndEvent(GlobalCoroutine coroutine, object userData);

            /// <summary>Occurs when created. Note that <see cref="GlobalCoroutine"/> is pooled, the same object instance will be used multiple times, and this event is called when the pooled instance is 'constructed', meaning this event will be called multiple times for the same object instance.</summary>
            public static CoroutineEvent onCreated;

            /// <summary>Occurs when a <see cref="GlobalCoroutine"/> is 'destroyed'. Note that <see cref="GlobalCoroutine"/> is pooled, the same object instance will be used multiple times, and this event is called when the pooled instance is 'destroyed', meaning this event will be called multiple times for the same object instance.</summary>
            public static CoroutineEvent onDestroyed;

            /// <summary>Occurs when a <see cref="GlobalCoroutine"/> is started.</summary>
            public static CoroutineEvent onCoroutineStarted;
            /// <summary>Occurs when a <see cref="GlobalCoroutine"/> is ended.</summary>
            public static CoroutineEvent onCoroutineEnded;

            /// <summary>Occurs before a subroutine in an executing <see cref="GlobalCoroutine"/> is started.</summary>
            /// <remarks>A user object can be returned, which is then passed to <see cref="onSubroutineEnd"/>.</remarks>
            public static CoroutineFrameStartEvent onSubroutineStart;

            /// <summary>Occurs when a subroutine in an executing <see cref="GlobalCoroutine"/> has ended.</summary>
            public static CoroutineFrameEndEvent onSubroutineEnd;

        }

    }

}
