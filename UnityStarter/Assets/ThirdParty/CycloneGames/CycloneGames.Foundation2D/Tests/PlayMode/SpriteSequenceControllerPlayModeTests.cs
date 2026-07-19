using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.Foundation2D.Runtime.Tests
{
    internal sealed class TestSpriteSequenceRenderer : MonoBehaviour, ISpriteSequenceRenderer
    {
        internal int LastAppliedFrame { get; private set; } = -1;
        internal int ApplyCount { get; private set; }
        internal bool Visible { get; private set; }
        internal IReadOnlyList<Sprite> Frames { get; private set; }

        public void Initialize(IReadOnlyList<Sprite> frames)
        {
            Frames = frames;
        }

        public void ApplyFrame(int frameIndex, bool forceRefresh)
        {
            LastAppliedFrame = frameIndex;
            ApplyCount++;
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
        }
    }

    public sealed class SpriteSequenceControllerPlayModeTests
    {
        private readonly List<Object> _createdObjects = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                if (_createdObjects[i] != null)
                {
                    Object.Destroy(_createdObjects[i]);
                }
            }

            _createdObjects.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GoToFrame_CommitsRendererBeforePublishingFrameEvent()
        {
            CreateController(2, out SpriteSequenceController controller, out TestSpriteSequenceRenderer renderer);
            yield return null;

            bool callbackCalled = false;
            bool callbackObservedCommittedVisual = false;
            int callbackFrame = -1;
            controller.OnFrameChanged += frame =>
            {
                callbackCalled = true;
                callbackFrame = frame;
                callbackObservedCommittedVisual = renderer.LastAppliedFrame == frame && renderer.Visible;
            };

            controller.GoToFrame(1);

            Assert.That(callbackCalled, Is.True);
            Assert.That(callbackFrame, Is.EqualTo(1));
            Assert.That(callbackObservedCommittedVisual, Is.True);
            Assert.That(controller.CurrentFrame, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SingleFrameOnce_RemainsVisibleForOneFrameDuration_ThenCompletes()
        {
            CreateController(1, out SpriteSequenceController controller, out TestSpriteSequenceRenderer renderer);
            SetPrivateField(controller, "frameRate", 1000f);
            SetPrivateField(controller, "playMode", SpriteSequenceController.PlayMode.Once);
            yield return null;

            bool completed = false;
            controller.OnPlayComplete += () => completed = true;
            controller.Play();

            Assert.That(controller.IsPlaying, Is.True);
            Assert.That(completed, Is.False);
            Assert.That(renderer.Visible, Is.True);

            for (int i = 0; i < 10 && !completed; i++)
            {
                yield return null;
            }

            Assert.That(completed, Is.True);
            Assert.That(controller.IsPlaying, Is.False);
            Assert.That(controller.CurrentFrame, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PauseAndResume_PreservePublicPlaybackStatus()
        {
            CreateController(2, out SpriteSequenceController controller, out _);
            yield return null;

            controller.Play();
            controller.Pause();
            Assert.That(controller.IsPaused, Is.True);
            Assert.That(controller.IsPlaying, Is.False);

            controller.Resume();
            Assert.That(controller.IsPlaying, Is.True);
            Assert.That(controller.IsPaused, Is.False);
        }

        private void CreateController(
            int frameCount,
            out SpriteSequenceController controller,
            out TestSpriteSequenceRenderer renderer)
        {
            var gameObject = new GameObject("Foundation2D.Controller.Test");
            gameObject.SetActive(false);
            _createdObjects.Add(gameObject);

            renderer = gameObject.AddComponent<TestSpriteSequenceRenderer>();
            controller = gameObject.AddComponent<SpriteSequenceController>();

            Texture2D texture = new(32, 16, TextureFormat.RGBA32, false);
            _createdObjects.Add(texture);
            var frames = new List<Sprite>(frameCount);
            for (int i = 0; i < frameCount; i++)
            {
                Sprite sprite = Sprite.Create(texture, new Rect(i * 16f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f));
                frames.Add(sprite);
                _createdObjects.Add(sprite);
            }

            SetPrivateField(controller, "frames", frames);
            SetPrivateField(controller, "rendererComponent", renderer);
            SetPrivateField(controller, "ignoreTimeScale", true);
            gameObject.SetActive(true);
        }

        private static void SetPrivateField<T>(SpriteSequenceController controller, string fieldName, T value)
        {
            FieldInfo field = typeof(SpriteSequenceController).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing test field: {fieldName}");
            field.SetValue(controller, value);
        }
    }
}
