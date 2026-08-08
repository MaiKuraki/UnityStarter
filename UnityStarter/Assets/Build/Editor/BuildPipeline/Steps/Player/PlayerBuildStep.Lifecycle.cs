using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed partial class PlayerBuildStep
    {
        private static Exception CombinePlayerBuildFailures(
            Exception playerBuildFailure,
            Exception sessionRestoreFailure)
        {
            if (playerBuildFailure == null)
            {
                return sessionRestoreFailure;
            }

            if (sessionRestoreFailure == null)
            {
                return playerBuildFailure;
            }

            return new AggregateException(
                "Player build and Player environment restoration both failed.",
                playerBuildFailure,
                sessionRestoreFailure);
        }

        internal static Exception DisposePlayerBuildSessions(
            IReadOnlyList<IDisposable> sessions)
        {
            if (sessions == null)
            {
                throw new ArgumentNullException(nameof(sessions));
            }

            Exception failure = null;
            for (int sessionIndex = sessions.Count - 1;
                 sessionIndex >= 0;
                 sessionIndex--)
            {
                IDisposable session = sessions[sessionIndex];
                if (session == null)
                {
                    continue;
                }

                try
                {
                    session.Dispose();
                }
                catch (Exception restoreException)
                {
                    failure = CombineSessionRestoreFailures(
                        failure,
                        restoreException);
                }
            }

            return failure;
        }

        private static Exception CombineSessionRestoreFailures(
            Exception existingFailure,
            Exception nextFailure)
        {
            if (existingFailure == null)
            {
                return nextFailure;
            }

            if (nextFailure == null)
            {
                return existingFailure;
            }

            return new AggregateException(
                "Multiple Player build environment sessions failed to restore.",
                existingFailure,
                nextFailure);
        }
    }
}

