// Copyright (c) CycloneGames
// Licensed under the MIT License.

using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioEventRouterTests
    {
        [Test]
        public void StartLoopingTrigger_RepeatedIndexHasSingleRegistration()
        {
            var owner = new GameObject("AudioEventRouterTests");

            try
            {
                AudioEventRouter router = owner.AddComponent<AudioEventRouter>();
                router.triggers = new[]
                {
                    new AudioTrigger
                    {
                        loopTrigger = true,
                        loopTimeMin = 10f,
                        loopTimeMax = 10f
                    }
                };

                router.StartLoopingTrigger(0);
                router.StartLoopingTrigger(0);

                Assert.AreEqual(1, router.ActiveLoopingTriggerCount);
                Assert.AreEqual(1, router.ActiveLoopingWorkerCount);

                // EditMode does not dispatch MonoBehaviour disable callbacks for a
                // non-ExecuteAlways component, so invoke the public lifecycle hook directly.
                router.OnDisable();
                Assert.AreEqual(0, router.ActiveLoopingTriggerCount);
                Assert.AreEqual(0, router.ActiveLoopingWorkerCount);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void StartLoopingTrigger_ReplacedTriggerTransfersRegistration()
        {
            var owner = new GameObject("AudioEventRouterTests");

            try
            {
                AudioEventRouter router = owner.AddComponent<AudioEventRouter>();
                var firstTrigger = new AudioTrigger
                {
                    loopTrigger = true,
                    loopTimeMin = 10f,
                    loopTimeMax = 10f
                };
                var replacementTrigger = new AudioTrigger
                {
                    loopTrigger = true,
                    loopTimeMin = 10f,
                    loopTimeMax = 10f
                };
                router.triggers = new[] { firstTrigger };

                router.StartLoopingTrigger(0);
                router.triggers[0] = replacementTrigger;
                router.StartLoopingTrigger(0);

                Assert.AreEqual(1, router.ActiveLoopingTriggerCount);
                Assert.AreEqual(1, router.ActiveLoopingWorkerCount);
                Assert.IsFalse(router.IsLoopingTriggerRegistered(0, firstTrigger));
                Assert.IsTrue(router.IsLoopingTriggerRegistered(0, replacementTrigger));

                router.OnDisable();
                Assert.AreEqual(0, router.ActiveLoopingTriggerCount);
                Assert.AreEqual(0, router.ActiveLoopingWorkerCount);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
