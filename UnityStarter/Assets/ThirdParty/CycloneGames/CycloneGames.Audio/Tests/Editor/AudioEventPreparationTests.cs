// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Reflection;
using System.Threading;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioEventPreparationTests
    {
        [Test]
        public void PublicContract_IsAvailableWithoutInternalActiveEventApis()
        {
            Type preparationType = typeof(AudioEventPreparation);
            MethodInfo beginMethod = typeof(ActiveEvent).GetMethod(
                nameof(ActiveEvent.BeginAsyncPreparation),
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo addSourceMethod = preparationType.GetMethod(
                nameof(AudioEventPreparation.TryAddSource),
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo completeMethod = preparationType.GetMethod(
                nameof(AudioEventPreparation.Complete),
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);

            Assert.IsTrue(preparationType.IsPublic);
            Assert.IsTrue(preparationType.IsSealed);
            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(preparationType));
            Assert.NotNull(beginMethod);
            Assert.AreEqual(preparationType, beginMethod.ReturnType);
            Assert.NotNull(preparationType.GetProperty(nameof(AudioEventPreparation.CancellationToken)));
            Assert.NotNull(addSourceMethod);
            Assert.NotNull(completeMethod);
            Assert.IsEmpty(preparationType.GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        }

        [Test]
        public void CancellationToken_RemainsRegistrableAfterEventStopsUntilScopeDisposes()
        {
            AudioEvent audioEvent = ScriptableObject.CreateInstance<AudioEvent>();
            var activeEvent = new ActiveEvent();
            AudioEventPreparation preparation = null;
            CancellationTokenRegistration registration = default;

            try
            {
                activeEvent.InitializeForManager(audioEvent, null);
                activeEvent.status = EventStatus.Preparing;
                preparation = activeEvent.BeginAsyncPreparation();
                Assert.NotNull(preparation);

                CancellationToken token = preparation.CancellationToken;
                activeEvent.ResetForPool();

                Assert.IsTrue(token.IsCancellationRequested);
                Assert.DoesNotThrow(() => registration = token.Register(() => { }));
            }
            finally
            {
                registration.Dispose();
                preparation?.Dispose();
                UnityEngine.Object.DestroyImmediate(audioEvent);
            }
        }

        // This mirrors the API surface available to a third-party AudioNode assembly. Keeping
        // the sample compiled prevents obsolete internal-only guidance from regressing.
        private static void CompileExternalNodeUsage(
            ActiveEvent activeEvent,
            AudioClip clip,
            IAudioClipHandle handle)
        {
            AudioEventPreparation preparation = activeEvent.BeginAsyncPreparation();
            if (preparation == null)
            {
                handle?.Release();
                return;
            }

            CancellationToken cancellationToken = preparation.CancellationToken;
            if (cancellationToken.IsCancellationRequested)
            {
                handle?.Release();
                preparation.Dispose();
                return;
            }

            bool accepted = preparation.TryAddSource(clip, clipHandle: handle);
            if (accepted)
                preparation.Complete();
            else
                preparation.Dispose();
        }
    }
}
