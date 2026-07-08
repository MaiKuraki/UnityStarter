using System.Collections.Generic;
using CycloneGames.Choreography.Core;
using NUnit.Framework;

namespace CycloneGames.Choreography.Tests
{
    [TestFixture]
    public sealed class ChoreographyBuilderTests
    {
        [Test]
        public void Build_CreatesSectionsTracksEventsStatesAndResourceReferences()
        {
            IChoreographyAsset asset = new ChoreographyBuilder("Fireball")
                .Section("Windup", 0.3d, section => section
                    .Interruptible(false)
                    .PreferredMode(ChoreographyPlaybackMode.Priority)
                    .Clock(ChoreographySectionClockSource.FixedFrame, frameRate: 30d)
                    .Track("Body", ChoreographyTrackKind.Animation, track => track
                        .Clip("WindupAnim", ChoreographyResources.Animation("Animations/Fireball/Windup"), 0d, 0.3d))
                    .Event("CastStarted", 0d)
                    .EventState("ChargeWindow", "Charge", 0d, 0.3d))
                .Section("Release", 0.2d, section => section
                    .Track("Audio", ChoreographyTrackKind.Audio, track => track
                        .Clip("ReleaseAudio", ChoreographyResources.AudioEvent("Play_Fireball", "CycloneGames.Audio", "Combat"), 0d, 0.2d)))
                .Build();

            Assert.AreEqual("Fireball", asset.Id);
            Assert.AreEqual(0.5d, asset.TotalDuration, 0.000000001d);
            Assert.AreEqual(2, asset.Sections.Count);

            ChoreographySection windup = asset.Sections[0];
            Assert.AreEqual("Windup", windup.Id);
            Assert.IsFalse(windup.Interruptible);
            Assert.AreEqual(ChoreographyPlaybackMode.Priority, windup.PreferredMode);
            Assert.AreEqual(ChoreographySectionClockSource.FixedFrame, windup.Clock.Source);
            Assert.AreEqual(30d, windup.Clock.FrameRate, 0.000000001d);
            Assert.AreEqual(1, windup.Tracks.Length);
            Assert.AreEqual(1, windup.Events.Length);
            Assert.AreEqual(1, windup.EventStates.Length);

            ChoreographyResourceReference audio = asset.Sections[1].Tracks[0].Clips[0].Resource;
            Assert.AreEqual(ChoreographyResourceKind.AudioEvent, audio.Kind);
            Assert.AreEqual("CycloneGames.Audio", audio.Provider);
            Assert.AreEqual("Combat", audio.Group);
        }

        [Test]
        public void CollectResourceReferences_DeduplicatesAgainstAssetAndExistingResults()
        {
            ChoreographyResourceReference shared = ChoreographyResources.Animation("Animations/Shared");
            IChoreographyAsset asset = new ChoreographyBuilder("Combo")
                .Section("A", 0.2d, section => section
                    .Track("Body", ChoreographyTrackKind.Animation, track => track
                        .Clip("A", shared, 0d, 0.2d)
                        .Clip("B", shared, 0d, 0.2d)))
                .Section("B", 0.2d, section => section
                    .Track("Audio", ChoreographyTrackKind.Audio, track => track
                        .Clip("Sfx", ChoreographyResources.AudioEvent("Play_Combo", "CycloneGames.Audio", "Combat"), 0d, 0d)))
                .Build();

            List<ChoreographyResourceReference> results = new List<ChoreographyResourceReference> { shared };
            int added = asset.CollectResourceReferences(results);

            Assert.AreEqual(1, added);
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(ChoreographyResourceKind.AudioEvent, results[1].Kind);
        }

        [Test]
        public void Constructors_CopyInputArrays()
        {
            ChoreographyClip clip = new ChoreographyClip(
                "A",
                ChoreographyResources.Animation("Animations/A"),
                0d,
                0.2d);
            ChoreographyClip[] clips = { clip };
            ChoreographyTrack track = new ChoreographyTrack("Body", ChoreographyTrackKind.Animation, clips);
            clips[0] = new ChoreographyClip("Mutated", default, 0d, 0d);

            Assert.AreEqual("A", track.Clips[0].Id);

            ChoreographyTrack[] tracks = { track };
            ChoreographySection section = new ChoreographySection("Windup", 0.2d, tracks);
            tracks[0] = new ChoreographyTrack("Mutated", ChoreographyTrackKind.Custom, null);

            Assert.AreEqual("Body", section.Tracks[0].Id);

            ChoreographySection[] sections = { section };
            BuiltChoreographyAsset asset = new BuiltChoreographyAsset("Safe", sections);
            sections[0] = new ChoreographySection("Mutated", 1d, null);

            Assert.AreEqual("Windup", asset.Sections[0].Id);
        }
    }
}
