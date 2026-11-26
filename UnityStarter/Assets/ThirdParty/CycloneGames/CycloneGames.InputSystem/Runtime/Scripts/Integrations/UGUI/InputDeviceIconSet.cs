using System;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>How a prompt entry obtains its sprite.</summary>
    public enum InputPromptIconSource : byte
    {
        /// <summary>Sprite stored in the asset. No provider and no loading step required.</summary>
        Direct = 0,

        /// <summary>Key resolved once through <see cref="IInputPromptIconProvider"/>.</summary>
        ProviderKey = 1
    }

    /// <summary>How a provider key is produced.</summary>
    public enum InputPromptKeySource : byte
    {
        /// <summary>Project asset. The path is kept in sync from the asset GUID.</summary>
        Asset = 0,

        /// <summary>Plain string key.</summary>
        CustomKey = 1
    }

    /// <summary>
    /// One prompt key: a custom string or a project asset with an optional sub-object. Carries every dimension a
    /// loader needs, so providers do not parse strings or guess sub-assets.
    /// </summary>
    public readonly struct InputPromptKey
    {
        public readonly string CustomKey;
        public readonly string AssetGuid;
        public readonly string AssetPath;
        public readonly string SubAssetName;
        public readonly long LocalFileId;

        public InputPromptKey(string customKey, string assetGuid, string assetPath, string subAssetName, long localFileId)
        {
            CustomKey = customKey;
            AssetGuid = assetGuid;
            AssetPath = assetPath;
            SubAssetName = subAssetName;
            LocalFileId = localFileId;
        }

        /// <summary>True when the key addresses a project asset rather than a custom string.</summary>
        public bool IsAsset => !string.IsNullOrEmpty(AssetGuid);

        /// <summary>True when the asset holds a sub-object, such as one sprite of a multiple-sprite texture or atlas.</summary>
        public bool HasSubAsset => LocalFileId != 0L || !string.IsNullOrEmpty(SubAssetName);
    }

    /// <summary>
    /// Product-owned icon source. Implementations may use Addressables, AssetRef or any bundle system; the input
    /// package only receives the resolved sprite.
    /// </summary>
    public interface IInputPromptIconProvider
    {
        /// <summary>Resolves one key. Returns false when the key has no sprite.</summary>
        /// <param name="key">Structured key stored in the icon set.</param>
        /// <param name="icon">Resolved sprite.</param>
        bool TryGet(in InputPromptKey key, out Sprite icon);
    }

    /// <summary>One prompt icon entry: a direct sprite or a provider key, selected by the owning icon set.</summary>
    [Serializable]
    public struct InputPromptIconRef
    {
        public Sprite direct;
        public InputPromptKeySource keySource;
        public string key;
        public string assetPath;
        public string assetGuid;
        public string subAssetName;
        public long localFileId;

        /// <summary>Structured key handed to the provider.</summary>
        public InputPromptKey Key => keySource == InputPromptKeySource.Asset
            ? new InputPromptKey(null, assetGuid, assetPath, subAssetName, localFileId)
            : new InputPromptKey(key, null, null, null, 0L);

        /// <summary>Resolves this entry for the given source.</summary>
        /// <param name="source">Source mode of the owning icon set.</param>
        /// <param name="provider">Provider, may be null in <see cref="InputPromptIconSource.Direct"/> mode.</param>
        /// <param name="icon">Resolved sprite, or null.</param>
        public bool TryResolve(InputPromptIconSource source, IInputPromptIconProvider provider, out Sprite icon)
        {
            if (source == InputPromptIconSource.Direct)
            {
                icon = direct;
                return icon != null;
            }

            icon = null;
            if (provider == null) return false;
            InputPromptKey promptKey = Key;
            bool hasKey = promptKey.IsAsset
                ? !string.IsNullOrEmpty(promptKey.AssetGuid)
                : !string.IsNullOrEmpty(promptKey.CustomKey);
            return hasKey && provider.TryGet(in promptKey, out icon) && icon != null;
        }
    }

    /// <summary>Sprite entry for one device layout, for example <c>DualShockGamepad</c>.</summary>
    [Serializable]
    internal struct DeviceLayoutIcon
    {
        public string layout;
        public InputPromptIconRef icon;
    }

    /// <summary>Sprite entry for one control path, for example <c>&lt;Gamepad&gt;/buttonSouth</c>.</summary>
    [Serializable]
    internal struct DeviceControlIcon
    {
        public string controlPath;
        public InputPromptIconRef icon;
    }

    /// <summary>
    /// Resolves prompt sprites in three steps: exact control path, device layout, then coarse device kind.
    /// Returns sprites only; display text belongs to the product's localization system, keyed by the same path.
    /// </summary>
    internal static class InputIconResolver
    {
        internal static bool TryResolve(
            string controlPath,
            InputDeviceKind kind,
            string deviceLayout,
            DeviceControlIcon[] controls,
            Sprite[] resolvedControls,
            DeviceLayoutIcon[] layouts,
            Sprite[] resolvedLayouts,
            Sprite keyboardMouseIcon,
            Sprite gamepadIcon,
            Sprite touchIcon,
            out Sprite icon)
        {
            if (!string.IsNullOrEmpty(controlPath) && controls != null && resolvedControls != null)
            {
                int count = Math.Min(controls.Length, resolvedControls.Length);
                for (int i = 0; i < count; i++)
                {
                    if (resolvedControls[i] != null &&
                        string.Equals(controls[i].controlPath, controlPath, StringComparison.OrdinalIgnoreCase))
                    {
                        icon = resolvedControls[i];
                        return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(deviceLayout) && layouts != null && resolvedLayouts != null)
            {
                int count = Math.Min(layouts.Length, resolvedLayouts.Length);
                for (int i = 0; i < count; i++)
                {
                    if (resolvedLayouts[i] != null &&
                        string.Equals(layouts[i].layout, deviceLayout, StringComparison.OrdinalIgnoreCase))
                    {
                        icon = resolvedLayouts[i];
                        return true;
                    }
                }
            }

            icon = ResolveKind(kind, keyboardMouseIcon, gamepadIcon, touchIcon);
            return icon != null;
        }

        internal static Sprite ResolveKind(InputDeviceKind kind, Sprite keyboardMouseIcon, Sprite gamepadIcon, Sprite touchIcon)
        {
            return kind switch
            {
                InputDeviceKind.Gamepad => gamepadIcon != null ? gamepadIcon : keyboardMouseIcon,
                InputDeviceKind.Touchscreen => touchIcon != null ? touchIcon : keyboardMouseIcon,
                _ => keyboardMouseIcon
            };
        }
    }

    /// <summary>
    /// Holds prompt sprites for device kinds, layouts and control paths. <see cref="InputPromptIconSource.Direct"/>
    /// works without any setup; <see cref="SetProvider"/> switches every entry to key-based resolution.
    /// </summary>
    [CreateAssetMenu(fileName = "InputDeviceIconSet", menuName = "CycloneGames/Input/Device Icon Set")]
    public class InputDeviceIconSet : ScriptableObject
    {
        [SerializeField] private InputPromptIconSource iconSource = InputPromptIconSource.Direct;
        [SerializeField] private InputPromptIconRef keyboardMouseIcon;
        [SerializeField] private InputPromptIconRef gamepadIcon;
        [SerializeField] private InputPromptIconRef touchIcon;

        [Header("Layout Prompts")]
        [SerializeField] private DeviceLayoutIcon[] layoutIcons = Array.Empty<DeviceLayoutIcon>();

        [Header("Control Prompts")]
        [SerializeField] private DeviceControlIcon[] controlIcons = Array.Empty<DeviceControlIcon>();

        private IInputPromptIconProvider _provider;

        /// <summary>Provider currently attached, exposed for tests.</summary>
        internal IInputPromptIconProvider Provider => _provider;

        /// <summary>True when resolved sprites are cached, exposed for tests.</summary>
        internal bool HasCachedSprites => _resolvedControlSprites != null || _resolvedLayoutSprites != null;
        private Sprite[] _resolvedLayoutSprites;
        private Sprite[] _resolvedControlSprites;
        private Sprite _resolvedKeyboardMouseIcon;
        private Sprite _resolvedGamepadIcon;
        private Sprite _resolvedTouchIcon;

        public Sprite KeyboardMouseIcon => keyboardMouseIcon.direct;
        public Sprite GamepadIcon => gamepadIcon.direct;
        public Sprite TouchIcon => touchIcon.direct;

        /// <summary>Resolved device fallback sprites, resolved on demand.</summary>
        public Sprite ResolvedKeyboardMouseIcon => _resolvedKeyboardMouseIcon;

        /// <summary>Resolved device fallback sprites, resolved on demand.</summary>
        public Sprite ResolvedGamepadIcon => _resolvedGamepadIcon;

        /// <summary>Resolved device fallback sprites, resolved on demand.</summary>
        public Sprite ResolvedTouchIcon => _resolvedTouchIcon;

        /// <summary>Key of the keyboard and mouse fallback icon.</summary>
        public InputPromptKey KeyboardMouseKey => keyboardMouseIcon.Key;

        /// <summary>Key of the gamepad fallback icon.</summary>
        public InputPromptKey GamepadKey => gamepadIcon.Key;

        /// <summary>Key of the touch fallback icon.</summary>
        public InputPromptKey TouchKey => touchIcon.Key;

        /// <summary>Source used by every prompt entry.</summary>
        public InputPromptIconSource Source => iconSource;

        /// <summary>Sets the key-based provider and invalidates cached sprites. Pass null to clear it.</summary>
        /// <param name="provider">Product-owned provider.</param>
        public void SetProvider(IInputPromptIconProvider provider)
        {
            _provider = provider;
            _resolvedControlSprites = null;
            _resolvedLayoutSprites = null;
            _resolvedKeyboardMouseIcon = null;
            _resolvedGamepadIcon = null;
            _resolvedTouchIcon = null;
        }

        /// <summary>
        /// Drops every cached sprite and the provider reference. Direct assets stay owned by this asset.
        /// Call before releasing provider handles; the next lookup resolves again.
        /// </summary>
        public void Release()
        {
            _provider = null;
            ClearCache();
        }

        /// <summary>Drops cached sprites when the asset is disabled or unloaded.</summary>
        private void OnDisable()
        {
            ClearCache();
        }

        private void ClearCache()
        {
            _resolvedControlSprites = null;
            _resolvedLayoutSprites = null;
            _resolvedKeyboardMouseIcon = null;
            _resolvedGamepadIcon = null;
            _resolvedTouchIcon = null;
        }

        /// <summary>Resolves every entry now and pins the sprites. Lookups otherwise resolve lazily.</summary>
        public void WarmupAll()
        {
            EnsureArrays();
            ResolveDevices();
            for (int i = 0; i < _resolvedControlSprites.Length; i++) ResolveControl(i);
            for (int i = 0; i < _resolvedLayoutSprites.Length; i++) ResolveLayout(i);
        }

        private void EnsureArrays()
        {
            if (_resolvedControlSprites == null)
            {
                _resolvedControlSprites = new Sprite[controlIcons != null ? controlIcons.Length : 0];
            }

            if (_resolvedLayoutSprites == null)
            {
                _resolvedLayoutSprites = new Sprite[layoutIcons != null ? layoutIcons.Length : 0];
            }
        }

        private void ResolveDevices()
        {
            if (_resolvedKeyboardMouseIcon == null) keyboardMouseIcon.TryResolve(iconSource, _provider, out _resolvedKeyboardMouseIcon);
            if (_resolvedGamepadIcon == null) gamepadIcon.TryResolve(iconSource, _provider, out _resolvedGamepadIcon);
            if (_resolvedTouchIcon == null) touchIcon.TryResolve(iconSource, _provider, out _resolvedTouchIcon);
        }

        private void ResolveControl(int index)
        {
            if (_resolvedControlSprites[index] == null)
            {
                controlIcons[index].icon.TryResolve(iconSource, _provider, out _resolvedControlSprites[index]);
            }
        }

        private void ResolveLayout(int index)
        {
            if (_resolvedLayoutSprites[index] == null)
            {
                layoutIcons[index].icon.TryResolve(iconSource, _provider, out _resolvedLayoutSprites[index]);
            }
        }

        /// <summary>Resolves a device icon from the coarse device kind.</summary>
        public Sprite GetIcon(InputDeviceKind kind)
        {
            ResolveDevices();
            return InputIconResolver.ResolveKind(kind, _resolvedKeyboardMouseIcon, _resolvedGamepadIcon, _resolvedTouchIcon);
        }

        /// <summary>Resolves a device icon from the layout first, then the coarse kind.</summary>
        public Sprite GetIcon(InputDeviceKind kind, string deviceLayout)
        {
            Resolve(null, kind, deviceLayout, out Sprite icon);
            return icon;
        }

        /// <summary>Resolves the icon of one control. Returns false when only a fallback exists.</summary>
        /// <param name="controlPath">Control path such as <c>&lt;Gamepad&gt;/buttonSouth</c>.</param>
        /// <param name="kind">Coarse device kind used by the final fallback.</param>
        /// <param name="deviceLayout">Concrete layout used before the coarse kind, or null.</param>
        /// <param name="icon">Resolved sprite, or null.</param>
        public bool TryGetIcon(string controlPath, InputDeviceKind kind, string deviceLayout, out Sprite icon)
        {
            return Resolve(controlPath, kind, deviceLayout, out icon);
        }

        /// <summary>Resolves a device icon from a device activity record.</summary>
        public Sprite GetIcon(InputDeviceActivity activity)
        {
            return GetIcon(activity.PromptKind, activity.DeviceLayout);
        }

#if UNITY_EDITOR
        /// <summary>Keeps stored paths in sync with asset GUIDs on import and save.</summary>
        private void OnValidate()
        {
            SyncAssetPath(ref keyboardMouseIcon);
            SyncAssetPath(ref gamepadIcon);
            SyncAssetPath(ref touchIcon);
            for (int i = 0; i < layoutIcons.Length; i++) SyncAssetPath(ref layoutIcons[i].icon);
            for (int i = 0; i < controlIcons.Length; i++) SyncAssetPath(ref controlIcons[i].icon);
        }

        private static void SyncAssetPath(ref InputPromptIconRef icon)
        {
            if (icon.keySource != InputPromptKeySource.Asset) return;
            if (!string.IsNullOrEmpty(icon.assetGuid))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(icon.assetGuid);
                if (!string.IsNullOrEmpty(path)) icon.assetPath = path;
            }
            else if (!string.IsNullOrEmpty(icon.assetPath))
            {
                icon.assetGuid = UnityEditor.AssetDatabase.AssetPathToGUID(icon.assetPath);
            }
        }
#endif

        private bool Resolve(string controlPath, InputDeviceKind kind, string deviceLayout, out Sprite icon)
        {
            EnsureArrays();
            ResolveDevices();
            if (!string.IsNullOrEmpty(controlPath))
            {
                for (int i = 0; i < controlIcons.Length; i++)
                {
                    if (string.Equals(controlIcons[i].controlPath, controlPath, StringComparison.OrdinalIgnoreCase))
                    {
                        ResolveControl(i);
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(deviceLayout))
            {
                for (int i = 0; i < layoutIcons.Length; i++)
                {
                    if (string.Equals(layoutIcons[i].layout, deviceLayout, StringComparison.OrdinalIgnoreCase))
                    {
                        ResolveLayout(i);
                        break;
                    }
                }
            }

            return InputIconResolver.TryResolve(
                controlPath, kind, deviceLayout,
                controlIcons, _resolvedControlSprites,
                layoutIcons, _resolvedLayoutSprites,
                _resolvedKeyboardMouseIcon, _resolvedGamepadIcon, _resolvedTouchIcon, out icon);
        }

    }
}
