using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Precedence and provider resolution of <see cref="InputDeviceIconSet"/>: exact control path, device layout,
    /// then coarse device kind.
    /// </summary>
    public sealed class InputDeviceIconSetTests
    {
        private Sprite _keyboard;
        private Sprite _gamepad;
        private Sprite _touch;
        private Sprite _south;
        private Sprite _playstation;
        private Sprite _provider;

        [SetUp]
        public void SetUp()
        {
            _keyboard = CreateSprite();
            _gamepad = CreateSprite();
            _touch = CreateSprite();
            _south = CreateSprite();
            _playstation = CreateSprite();
            _provider = CreateSprite();
        }

        [TearDown]
        public void TearDown()
        {
            Destroy(_keyboard); Destroy(_gamepad); Destroy(_touch);
            Destroy(_south); Destroy(_playstation); Destroy(_provider);
        }

        [Test]
        public void DirectSource_ResolvesWithoutAProvider()
        {
            var controls = new[] { new DeviceControlIcon { controlPath = "<Gamepad>/buttonSouth", icon = new InputPromptIconRef { direct = _south } } };
            var layouts = new[] { new DeviceLayoutIcon { layout = "DualShockGamepad", icon = new InputPromptIconRef { direct = _playstation } } };

            Assert.That(Invoke("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad, "DualShockGamepad",
                controls, layouts, InputPromptIconSource.Direct, null, out Sprite icon), Is.True);
            Assert.That(icon, Is.SameAs(_south));

            Assert.That(Invoke("<Gamepad>/buttonEast", InputDeviceKind.Gamepad, "DualShockGamepad",
                controls, layouts, InputPromptIconSource.Direct, null, out icon), Is.True);
            Assert.That(icon, Is.SameAs(_playstation));

            Assert.That(Invoke(null, InputDeviceKind.Touchscreen, null,
                controls, layouts, InputPromptIconSource.Direct, null, out icon), Is.True);
            Assert.That(icon, Is.SameAs(_touch));
        }

        [Test]
        public void ProviderKey_ResolvesOnceAndCaches()
        {
            var provider = new FakeProvider(new Dictionary<string, Sprite> { { "prompt/a", _provider } });
            var controls = new[] { new DeviceControlIcon { controlPath = "<Gamepad>/buttonSouth", icon = new InputPromptIconRef { keySource = InputPromptKeySource.CustomKey, key = "prompt/a" } } };

            Assert.That(Invoke("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad, null,
                controls, null, InputPromptIconSource.ProviderKey, provider, out Sprite icon), Is.True);
            Assert.That(icon, Is.SameAs(_provider));
            Assert.That(provider.Calls, Is.EqualTo(1));
        }

        [Test]
        public void MissingProvider_FallsBackToTheKind()
        {
            var controls = new[] { new DeviceControlIcon { controlPath = "<Gamepad>/buttonSouth", icon = new InputPromptIconRef { keySource = InputPromptKeySource.CustomKey, key = "prompt/a" } } };

            Assert.That(Invoke("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad, null,
                controls, null, InputPromptIconSource.ProviderKey, null, out Sprite icon), Is.True);
            Assert.That(icon, Is.SameAs(_gamepad));
        }

        [Test]
        public void EmptyEverything_ReturnsFalse()
        {
            Assert.That(Invoke("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad, null,
                null, null, InputPromptIconSource.Direct, null, null, null, null, out Sprite icon), Is.False);
            Assert.That(icon, Is.Null);
        }

        [Test]
        public void Matching_IsCaseInsensitive()
        {
            var controls = new[] { new DeviceControlIcon { controlPath = "<Gamepad>/buttonSouth", icon = new InputPromptIconRef { direct = _south } } };

            Assert.That(Invoke("<gamepad>/buttonsouth", InputDeviceKind.Gamepad, null,
                controls, null, InputPromptIconSource.Direct, null, out Sprite icon), Is.True);
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
                var controlMap = new Dictionary<string, Sprite>();
                for (int i = 0; i < paths.Length; i++)
                {
                    if (random.Next(3) == 0) continue;
                    controls[i] = new DeviceControlIcon { controlPath = paths[i], icon = new InputPromptIconRef { direct = _south } };
                    controlMap[paths[i].ToLowerInvariant()] = _south;
                }

                var layoutEntries = new DeviceLayoutIcon[layouts.Length];
                var layoutMap = new Dictionary<string, Sprite>();
                for (int i = 0; i < layouts.Length; i++)
                {
                    if (random.Next(3) == 0) continue;
                    layoutEntries[i] = new DeviceLayoutIcon { layout = layouts[i], icon = new InputPromptIconRef { direct = _playstation } };
                    layoutMap[layouts[i].ToLowerInvariant()] = _playstation;
                }

                for (int step = 0; step < 500; step++)
                {
                    string path = random.Next(5) == 0 ? null : paths[random.Next(paths.Length)];
                    string layout = random.Next(5) == 0 ? null : layouts[random.Next(layouts.Length)];
                    InputDeviceKind kind = (InputDeviceKind)random.Next(0, 4);

                    Sprite expected;
                    if (path != null && controlMap.TryGetValue(path.ToLowerInvariant(), out Sprite controlIcon)) expected = controlIcon;
                    else if (layout != null && layoutMap.TryGetValue(layout.ToLowerInvariant(), out Sprite layoutIcon)) expected = layoutIcon;
                    else expected = kind == InputDeviceKind.Gamepad ? _gamepad : kind == InputDeviceKind.Touchscreen ? _touch : _keyboard;

                    Assert.That(Invoke(path, kind, layout, controls, layoutEntries, InputPromptIconSource.Direct, null, out Sprite actual), Is.True);
                    Assert.That(actual, Is.SameAs(expected), "round " + round + " step " + step);
                }
            }
        }

        private bool Invoke(string path, InputDeviceKind kind, string layout,
            DeviceControlIcon[] controls, DeviceLayoutIcon[] layouts, InputPromptIconSource source,
            IInputPromptIconProvider provider, out Sprite icon)
        {
            Sprite[] resolvedControls = ResolveSprites(controls, source, provider);
            Sprite[] resolvedLayouts = ResolveSprites(layouts, source, provider);
            return InputIconResolver.TryResolve(path, kind, layout, controls, resolvedControls, layouts, resolvedLayouts,
                _keyboard, _gamepad, _touch, out icon);
        }

        private bool Invoke(string path, InputDeviceKind kind, string layout,
            DeviceControlIcon[] controls, DeviceLayoutIcon[] layouts, InputPromptIconSource source,
            IInputPromptIconProvider provider, Sprite keyboard, Sprite gamepad, Sprite touch, out Sprite icon)
        {
            Sprite[] resolvedControls = ResolveSprites(controls, source, provider);
            Sprite[] resolvedLayouts = ResolveSprites(layouts, source, provider);
            return InputIconResolver.TryResolve(path, kind, layout, controls, resolvedControls, layouts, resolvedLayouts,
                keyboard, gamepad, touch, out icon);
        }

        [Test]
        public void ProviderKeySources_PassTheSelectedKeyToTheProvider()
        {
            var provider = new FakeProvider(new Dictionary<string, Sprite>
            {
                { "custom/key", _provider },
                { "0123456789abcdef:0", _provider }
            });

            var custom = new Runtime.InputPromptIconRef { keySource = Runtime.InputPromptKeySource.CustomKey, key = "custom/key" };
            var asset = new Runtime.InputPromptIconRef
            {
                keySource = Runtime.InputPromptKeySource.Asset,
                assetPath = "Assets/UI/Icons/a.png",
                assetGuid = "0123456789abcdef"
            };

            Assert.That(custom.Key.CustomKey, Is.EqualTo("custom/key"));
            Assert.That(asset.Key.AssetGuid, Is.EqualTo("0123456789abcdef"));
            Assert.That(asset.Key.AssetPath, Is.EqualTo("Assets/UI/Icons/a.png"));

            Assert.That(custom.TryResolve(Runtime.InputPromptIconSource.ProviderKey, provider, out Sprite icon), Is.True);
            Assert.That(asset.TryResolve(Runtime.InputPromptIconSource.ProviderKey, provider, out icon), Is.True);
            Assert.That(icon, Is.SameAs(_provider));
            Assert.That(provider.Calls, Is.EqualTo(2));
        }

        [Test]
        public void Release_DropsProviderAndCachesAndIsIdempotent()
        {
            var set = ScriptableObject.CreateInstance<InputDeviceIconSet>();
            try
            {
                var provider = new FakeProvider(new Dictionary<string, Sprite>());
                set.SetProvider(provider);
                Assert.That(set.Provider, Is.SameAs(provider));

                set.Release();
                Assert.That(set.Provider, Is.Null);
                Assert.That(set.HasCachedSprites, Is.False);

                set.Release();
                Assert.That(set.Provider, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(set);
            }
        }

        [Test]
        public void DestroyedCachedSprite_IsTreatedAsMissing()
        {
            var controls = new[] { new DeviceControlIcon { controlPath = "<Gamepad>/buttonSouth", icon = new InputPromptIconRef { direct = _south } } };
            var cached = new[] { _south };
            Destroy(_south);

            Assert.That(InputIconResolver.TryResolve("<Gamepad>/buttonSouth", InputDeviceKind.Gamepad, null,
                controls, cached, null, null, _keyboard, _gamepad, _touch, out Sprite icon), Is.True);
            Assert.That(icon, Is.SameAs(_gamepad), "a destroyed sprite must fall through to the kind fallback");
        }

        private static Sprite[] ResolveSprites(DeviceControlIcon[] entries, InputPromptIconSource source, IInputPromptIconProvider provider)
        {
            if (entries == null) return null;
            var sprites = new Sprite[entries.Length];
            for (int i = 0; i < entries.Length; i++) entries[i].icon.TryResolve(source, provider, out sprites[i]);
            return sprites;
        }

        private static Sprite[] ResolveSprites(DeviceLayoutIcon[] entries, InputPromptIconSource source, IInputPromptIconProvider provider)
        {
            if (entries == null) return null;
            var sprites = new Sprite[entries.Length];
            for (int i = 0; i < entries.Length; i++) entries[i].icon.TryResolve(source, provider, out sprites[i]);
            return sprites;
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

        private sealed class FakeProvider : IInputPromptIconProvider
        {
            private readonly Dictionary<string, Sprite> _sprites;
            public int Calls;
            public FakeProvider(Dictionary<string, Sprite> sprites) { _sprites = sprites; }
            public bool TryGet(in Runtime.InputPromptKey key, out Sprite icon)
            {
                Calls++;
                string id = key.IsAsset ? key.AssetGuid + ":" + key.LocalFileId : key.CustomKey ?? string.Empty;
                return _sprites.TryGetValue(id, out icon);
            }
        }
    }
}
