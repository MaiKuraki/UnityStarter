using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Foundation2D.Runtime.Tests
{
    public sealed class SpriteFlipbookCompatibilityTests
    {
        private readonly List<Object> _createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                if (_createdObjects[i] != null)
                {
                    Object.DestroyImmediate(_createdObjects[i]);
                }
            }

            _createdObjects.Clear();
        }

        [Test]
        public void CompatibleFullRectFrames_BuildNormalizedUvRects()
        {
            Texture2D texture = CreateTexture(64, 32);
            Sprite first = CreateSprite(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f));
            Sprite second = CreateSprite(texture, new Rect(16f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f));
            var output = new Vector4[2];

            bool compatible = SpriteFlipbookCompatibility.TryValidateAndBuild(
                new[] { first, second },
                output,
                out Vector4 baseRect,
                out SpriteFlipbookCompatibilityError error,
                out int errorFrameIndex);

            Assert.That(compatible, Is.True);
            Assert.That(error, Is.EqualTo(SpriteFlipbookCompatibilityError.None));
            Assert.That(errorFrameIndex, Is.EqualTo(-1));
            Assert.That(baseRect, Is.EqualTo(new Vector4(0f, 0f, 0.25f, 0.5f)));
            Assert.That(output[1], Is.EqualTo(new Vector4(0.25f, 0f, 0.25f, 0.5f)));
        }

        [Test]
        public void DifferentTexture_IsRejected()
        {
            Texture2D firstTexture = CreateTexture(32, 32);
            Texture2D secondTexture = CreateTexture(32, 32);
            Sprite first = CreateSprite(firstTexture, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f));
            Sprite second = CreateSprite(secondTexture, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f));

            bool compatible = SpriteFlipbookCompatibility.TryValidateAndBuild(
                new[] { first, second },
                new Vector4[2],
                out _,
                out SpriteFlipbookCompatibilityError error,
                out int errorFrameIndex);

            Assert.That(compatible, Is.False);
            Assert.That(error, Is.EqualTo(SpriteFlipbookCompatibilityError.TextureMismatch));
            Assert.That(errorFrameIndex, Is.EqualTo(1));
        }

        [Test]
        public void DifferentPivot_IsRejected()
        {
            Texture2D texture = CreateTexture(32, 32);
            Sprite first = CreateSprite(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f));
            Sprite second = CreateSprite(texture, new Rect(16f, 0f, 16f, 16f), new Vector2(0.25f, 0.5f));

            bool compatible = SpriteFlipbookCompatibility.TryValidateAndBuild(
                new[] { first, second },
                new Vector4[2],
                out _,
                out SpriteFlipbookCompatibilityError error,
                out int errorFrameIndex);

            Assert.That(compatible, Is.False);
            Assert.That(error, Is.EqualTo(SpriteFlipbookCompatibilityError.PivotMismatch));
            Assert.That(errorFrameIndex, Is.EqualTo(1));
        }

        [Test]
        public void DifferentRectSize_IsRejected()
        {
            Texture2D texture = CreateTexture(64, 32);
            Sprite first = CreateSprite(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f));
            Sprite second = CreateSprite(texture, new Rect(16f, 0f, 24f, 16f), new Vector2(0.5f, 0.5f));

            bool compatible = SpriteFlipbookCompatibility.TryValidateAndBuild(
                new[] { first, second },
                new Vector4[2],
                out _,
                out SpriteFlipbookCompatibilityError error,
                out int errorFrameIndex);

            Assert.That(compatible, Is.False);
            Assert.That(error, Is.EqualTo(SpriteFlipbookCompatibilityError.RectSizeMismatch));
            Assert.That(errorFrameIndex, Is.EqualTo(1));
        }

        private Texture2D CreateTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _createdObjects.Add(texture);
            return texture;
        }

        private Sprite CreateSprite(Texture2D texture, Rect rect, Vector2 pivot)
        {
            Sprite sprite = Sprite.Create(texture, rect, pivot, 100f, 0, SpriteMeshType.FullRect);
            _createdObjects.Add(sprite);
            return sprite;
        }
    }
}
