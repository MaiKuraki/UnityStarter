using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Precedence of the icon resolver: exact control path, then device layout, then the coarse device kind.
    /// </summary>
    public sealed class InputDeviceIconSetTests
    {
        private Sprite _keyboard;
        private Sprite _gamepad;
        private Sprite _touch;
        private Sprite _south;
        private Sprite _playstation;

        [SetUp]
        public void SetUp()
        {
            _keyboard = CreateSprite();
            _gamepad = CreateSprite();
            _touch = CreateSprite();
            _south = CreateSprite();
            _playstation = CreateSprite();
        }

        [TearDown]
        public void TearDown()
        {
            Destroy(_keyboard);
            Destroy(_gamepad);
            Destroy(_touch);
            Destroy(_south);
            Destroy(_playstation);
        }

        [Test]
        public void ControlPath_WinsOverLayoutAndKind()
        {
            var layouts = new[] { new DeviceLayoutIcon { layout = "DualShockGamepad", icon = _playstation } };
            var controls = new[] { new DeviceControlIcon { controlPath = "<Gamepad>/buttonSouth", icon = _south } };

            Assert.That(InputIconResolver.TryResolve("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad,
                "DualShockGamepad", controls, layouts, _keyboard, _gamepad, _touch, out Sprite icon), Is.True);
            Assert.That(icon, Is.SameAs(_south));
        }

        [Test]
        public void Layout_WinsOverKind()
        {
            var layouts = new[] { new DeviceLayoutIcon { layout = "DualShockGamepad", icon = _playstation } };

            Assert.That(InputIconResolver.TryResolve("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad,
                "DualShockGamepad", null, layouts, _keyboard, _gamepad, _touch, out Sprite icon), Is.True);
            Assert.That(icon, Is.SameAs(_playstation));
        }

        [Test]
        public void Kind_IsTheFinalFallback()
        {
            Assert.That(InputIconResolver.TryResolve("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad,
                "XInputController", null, null, _keyboard, _gamepad, _touch, out Sprite icon), Is.True);
            Assert.That(icon, Is.SameAs(_gamepad));

            Assert.That(InputIconResolver.TryResolve(null, InputDeviceKind.Touchscreen,
                null, null, null, _keyboard, _gamepad, _touch, out icon), Is.True);
            Assert.That(icon, Is.SameAs(_touch));
        }

        [Test]
        public void MissingEverything_ReturnsFalse()
        {
            Assert.That(InputIconResolver.TryResolve("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad,
                "XInputController", null, null, null, null, null, out Sprite icon), Is.False);
            Assert.That(icon, Is.Null);
        }

        [Test]
        public void Matching_IsCaseInsensitive()
        {
            var controls = new[] { new DeviceControlIcon { controlPath = "<Gamepad>/buttonSouth", icon = _south } };

            Assert.That(InputIconResolver.TryResolve("<gamepad>/buttonsouth", InputDeviceKind.Gamepad,
                null, controls, null, _keyboard, _gamepad, _touch, out Sprite icon), Is.True);
            Assert.That(icon, Is.SameAs(_south));
        }

        [Test]
        public void Fuzz_PrecedenceMatchesADictionaryLookup()
        {
            var random = new System.Random(930017);
            string[] paths = { "<Gamepad>/buttonSouth", "<Gamepad>/buttonEast", "<Keyboard>/space", "<Mouse>/leftButton" };
            string[] layouts = { "XInputController", "DualShockGamepad", "Keyboard" };

            for (int round = 0; round < 4; round++)
            {
                var controls = new DeviceControlIcon[paths.Length];
                var controlMap = new System.Collections.Generic.Dictionary<string, Sprite>();
                for (int i = 0; i < paths.Length; i++)
                {
                    if (random.Next(3) == 0) continue;
                    controls[i] = new DeviceControlIcon { controlPath = paths[i], icon = _south };
                    controlMap[paths[i].ToLowerInvariant()] = _south;
                }

                var layoutEntries = new DeviceLayoutIcon[layouts.Length];
                var layoutMap = new System.Collections.Generic.Dictionary<string, Sprite>();
                for (int i = 0; i < layouts.Length; i++)
                {
                    if (random.Next(3) == 0) continue;
                    layoutEntries[i] = new DeviceLayoutIcon { layout = layouts[i], icon = _playstation };
                    layoutMap[layouts[i].ToLowerInvariant()] = _playstation;
                }

                for (int step = 0; step < 500; step++)
                {
                    string path = random.Next(5) == 0 ? null : paths[random.Next(paths.Length)];
                    string layout = random.Next(5) == 0 ? null : layouts[random.Next(layouts.Length)];
                    InputDeviceKind kind = (InputDeviceKind)random.Next(0, 4);

                    Sprite expected;
                    if (path != null && controlMap.TryGetValue(path.ToLowerInvariant(), out Sprite controlIcon))
                    {
                        expected = controlIcon;
                    }
                    else if (layout != null && layoutMap.TryGetValue(layout.ToLowerInvariant(), out Sprite layoutIcon))
                    {
                        expected = layoutIcon;
                    }
                    else
                    {
                        expected = kind == InputDeviceKind.Gamepad ? _gamepad : kind == InputDeviceKind.Touchscreen ? _touch : _keyboard;
                    }

                    bool resolved = InputIconResolver.TryResolve(path, kind, layout, controls, layoutEntries,
                        _keyboard, _gamepad, _touch, out Sprite actual);
                    Assert.That(actual, Is.SameAs(expected), "round " + round + " step " + step);
                    Assert.That(resolved, Is.True);
                }
            }
        }

        private static Sprite CreateSprite()
        {
            var texture = new Texture2D(1, 1);
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), Vector2.zero);
        }

        private static void Destroy(Sprite sprite)
        {
            if (sprite == null) return;
            Texture2D texture = sprite.texture;
            Object.DestroyImmediate(sprite);
            if (texture != null) Object.DestroyImmediate(texture);
        }
    }
}
