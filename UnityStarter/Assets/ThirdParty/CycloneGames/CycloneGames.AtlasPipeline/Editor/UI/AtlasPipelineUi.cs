using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// Cached IMGUI resources for the Atlas Pipeline window: GUIContent, option arrays, styles and
    /// layout options.
    /// OnGUI runs several times per frame (layout, repaint, one pass per input event), so anything
    /// constructed inside a draw method allocates on every one of those passes. Every GUIContent
    /// here is built once; draw code references the fields directly and allocates nothing.
    /// Add new cached entries here instead of writing <c>new GUIContent</c> in draw code.
    /// </summary>
    internal static class AtlasPipelineUi
    {
        /// <summary>
        /// Numeric atlas size options, mirrored from the window's MaxTextureSizeOptions. Both this
        /// array and the window's were sourced from the same values; the window owns the canonical
        /// int[] and this builds GUIContent from it once at first use.
        /// </summary>
        /// <summary>
        /// Atlas and source size options. Every value is a power of two, which is the real
        /// requirement: block-compressed formats need dimensions that are a multiple of their block
        /// size, and any power of two at or above 4 is already a multiple of 4 — so 512 and 2048 are
        /// exactly as block-aligned as 256 and 1024. Restricting this to powers of FOUR would drop
        /// 512 and 2048 for no benefit.
        /// 4096 is included because a large project may prefer fewer, larger atlas files; the cost
        /// is 4x the memory of 2048, which validation reports rather than blocks.
        /// </summary>
        internal static readonly int[] SizeValues = { 256, 512, 1024, 2048, 4096 };

        // ── General panel ───────────────────────────────────────────────────────

        public static readonly GUIContent AutoImport = new GUIContent("Auto Import Sprites");
        public static readonly GUIContent AutoGenerateAtlases =
            new GUIContent("Auto Generate Atlases");

        public static readonly GUIContent AsciiOnlyNames = new GUIContent(
            "ASCII-Only Names",
            "When enabled, atlas source file names may only contain ASCII letters, "
            + "digits, underscores and dashes. Non-ASCII names (Chinese, full-width "
            + "characters, emoji) enter the rename review flow and block the build "
            + "validation. Recommended for multi-platform projects.");

        public static readonly GUIContent AtlasKeyCasing = new GUIContent(
            "Atlas Key Casing",
            "Lower makes every generated atlas file name lowercase, so the name follows "
            + "from the rule configuration instead of depending on which spelling was "
            + "indexed first. Changing this renames existing atlas files.");

        public static readonly GUIContent CollisionSafeKeys = new GUIContent(
            "Collision Safe Keys",
            "PerSprite atlas keys include the folder path, so 'UI/a/btn.png' and "
            + "'UI/b/btn.png' land in different atlases instead of silently merging. "
            + "Changing this renames every PerSprite atlas file.");

        public static readonly GUIContent AutoPageOverflowing = new GUIContent(
            "Auto Page Overflowing",
            "When on, an atlas that cannot fit its max texture size is split into page "
            + "files (key__p000, key__p001, ...). Atlases that already fit keep their "
            + "exact current file name, so enabling this changes no existing output.");

        public static readonly GUIContent OutputAtlasFolder = new GUIContent(
            "Default Output Folder",
            "The root every atlas is written under. Rules without an Output Subfolder "
            + "write here directly; a rule with one writes to this folder plus its "
            + "subfolder. The whole tree is ignored as a rule source — the pipeline's "
            + "output can never feed back into its input.");

        // ── Packing panel ───────────────────────────────────────────────────────

        public static readonly GUIContent Padding = new GUIContent(
            "Padding",
            "Pixels of empty space between sprites. Filtering and block compression "
            + "sample past sprite edges, so zero padding makes neighbouring sprites bleed "
            + "into each other.");

        public static readonly GUIContent BlockOffset = new GUIContent("Block Offset");

        public static readonly GUIContent RotationDefault = new GUIContent(
            "Atlas Rotation Default",
            "Global default. Per-rule overrides live on each rule's Atlas Rotation.");

        public static readonly GUIContent TightPacking = new GUIContent(
            "Tight Packing",
            "Pack to the sprite's polygon outline instead of its bounding rectangle.");

        public static readonly GUIContent AlphaDilationDefault = new GUIContent(
            "Alpha Dilation",
            "Global default. Per-rule overrides live on each rule's Alpha Dilation.");

        public static readonly GUIContent IncludeInBuildDefault = new GUIContent(
            "Include In Build",
            "Global default for whether atlases are baked into the player build. "
            + "Per-rule overrides live on each rule's Include In Build.");

        // ── Rule editor: identity and direct settings ──────────────────────────

        public static readonly GUIContent RuleName = new GUIContent("Rule Name");
        public static readonly GUIContent AtlasGroup = new GUIContent("Atlas Group");

        public static readonly GUIContent SourceFolder = new GUIContent(
            "Source Folder",
            "The folder reference is stored by GUID, so renaming the folder "
            + "in the Project window keeps the rule pointing at it.");

        public static readonly GUIContent OutputSubfolder = new GUIContent(
            "Output Subfolder",
            "Drag a folder here: this rule's atlases are written under the default "
            + "output folder, inside the folder you drop. Leave empty (or drop the root "
            + "itself) to write to the root. Rules that name the same subfolder share "
            + "one package, which is how a project splits its atlases across asset "
            + "packages for hot update. The value is stored relative to the output root, "
            + "so a rule can never write outside it.");

        public static readonly GUIContent SpriteMode = new GUIContent("Sprite Mode");
        public static readonly GUIContent PixelsPerUnit = new GUIContent("Pixels Per Unit");

        public static readonly GUIContent PixelArt = new GUIContent(
            "Pixel Art",
            "Uncompressed + Point. Forces both the source texture and generated "
            + "atlas to RGBA32 (uncompressed) on all platforms, avoiding "
            + "compressed-source packing artifacts, and forces Point filtering on "
            + "the atlas texture and its sources — the atlas is what renders at "
            + "runtime, so this is what keeps pixels crisp.");

        public static readonly GUIContent FilterMode = new GUIContent(
            "Filter Mode",
            "Filtering for the packed atlas texture and its sources. Overridden "
            + "to Point while Pixel Art is on.");
        public static readonly GUIContent WrapMode = new GUIContent("Wrap Mode");
        public static readonly GUIContent Mipmaps = new GUIContent("Mipmaps");
        public static readonly GUIContent Readable = new GUIContent("Readable");
        public static readonly GUIContent CompressionQuality =
            new GUIContent("Compression Quality");
        public static readonly GUIContent AtlasGranularity = new GUIContent("Atlas Granularity");

        public static readonly GUIContent AtlasMax = new GUIContent(
            "Atlas Max",
            "Shared atlas size for this rule. The per-platform size overrides below "
            + "inherit from this value when set to Inherit.");

        public static readonly GUIContent RecommendedMax = new GUIContent("Recommended Max");

        public static readonly GUIContent WarnTextureSize = new GUIContent(
            "Warn Texture Size",
            "When enabled, oversized source textures are reported in a single dialog.");

        public static readonly GUIContent AtlasRotationRule = new GUIContent(
            "Atlas Rotation",
            "Inherit uses the global default; Enabled forces rotation; "
            + "Disabled disables rotation for this rule. Pixel Art rules "
            + "always disable rotation to avoid non-integer texel sampling.");

        public static readonly GUIContent RecommendedMaxPopup = new GUIContent(
            "Recommended Max",
            "Maximum source texture size before the importer warns the developer.");

        public static readonly GUIContent AtlasMaxPopup = new GUIContent(
            "Atlas Max",
            "Maximum generated SpriteAtlas texture size.");

        // ── Per-platform labels ─────────────────────────────────────────────────

        public static readonly GUIContent Android = new GUIContent("Android");
        public static readonly GUIContent Iphone = new GUIContent("iPhone");
        public static readonly GUIContent Webgl = new GUIContent("WebGL");
        public static readonly GUIContent Standalone = new GUIContent("Standalone");

        /// <summary>Indexed by <see cref="AtlasPlatform"/>. Labels only, no tooltips.</summary>
        public static readonly GUIContent[] PlatformLabels =
        {
            Android,
            Iphone,
            Webgl,
            Standalone,
        };

        // ── Popup option sets ───────────────────────────────────────────────────

        /// <summary>The three states of a per-rule override popup.</summary>
        public static readonly GUIContent[] ToggleOptions =
        {
            new GUIContent("Inherit"),
            new GUIContent("On"),
            new GUIContent("Off"),
        };

        /// <summary>
        /// Numeric size options, aligned with AtlasPipelineWindow.MaxTextureSizeOptions: entry i
        /// corresponds to MaxTextureSizeOptions[i]. Built once from the same source array so the
        /// two can never disagree.
        /// Declaration order matters: static initializers run top to bottom, so this must be
        /// declared before <see cref="SizePopupOptions"/>, which is built from it.
        /// </summary>
        public static readonly GUIContent[] SizeOptions = BuildSizeOptions();

        private static GUIContent[] BuildSizeOptions()
        {
            var options = new GUIContent[SizeValues.Length];
            for (int i = 0; i < SizeValues.Length; i++)
            {
                options[i] = new GUIContent(SizeValues[i].ToString());
            }

            return options;
        }

        /// <summary>
        /// The Inherit entry of the per-platform size popup: uses the rule's shared Atlas Max.
        /// </summary>
        public static readonly GUIContent SizeInheritOption = new GUIContent(
            "Inherit",
            "Use the rule's shared Atlas Max value.");

        /// <summary>
        /// Full option set for the per-platform size popup: Inherit followed by the numeric sizes.
        /// Prebuilt so the popup draws without allocating an options array per call.
        /// </summary>
        public static readonly GUIContent[] SizePopupOptions = BuildSizePopupOptions();

        private static GUIContent[] BuildSizePopupOptions()
        {
            var options = new GUIContent[1 + SizeOptions.Length];
            options[0] = SizeInheritOption;
            for (int i = 0; i < SizeOptions.Length; i++)
            {
                options[i + 1] = SizeOptions[i];
            }

            return options;
        }

        // ── Per-rule override popups (rule editor) ─────────────────────────────

        public static readonly GUIContent IncludeInBuildOverride = new GUIContent(
            "In Build",
            "Inherit uses the global Include In Build. Force Off for atlases that ship in "
            + "asset packages (YooAsset / Addressables) instead of the installer. Note: with "
            + "it off, sprites referenced by installer-baked scenes fall back to their "
            + "individual textures in the base package.");

        public static readonly GUIContent AlphaDilationOverride = new GUIContent(
            "Alpha Dilation",
            "Inherit uses the global Alpha Dilation. Dilation fills the padding around each "
            + "sprite with its own edge colour, which prevents filter and compression seams; "
            + "it never modifies the sprite's own pixels. Turn off for point-filtered pixel "
            + "art, where nothing samples the padding anyway.");

        // ── Per-platform format popup labels (rule editor) ─────────────────────

        public static readonly GUIContent FormatAndroid = new GUIContent(
            "Android", "Texture format used by the Android player.");
        public static readonly GUIContent FormatIphone = new GUIContent(
            "iPhone", "Texture format used by the iOS player.");
        public static readonly GUIContent FormatWebgl = new GUIContent(
            "WebGL", "Texture format used by the WebGL player.");
        public static readonly GUIContent FormatStandalone = new GUIContent(
            "Standalone", "Texture format used by the desktop player.");

        /// <summary>
        /// Format popup options per platform, built once from the static supported-format tables.
        /// Indexed by <see cref="AtlasPlatform"/>.
        /// </summary>
        private static readonly GUIContent[][] s_formatOptions = BuildFormatOptions();

        public static GUIContent[] GetFormatOptions(AtlasPlatform platform)
        {
            return s_formatOptions[(int)platform];
        }

        private static GUIContent[][] BuildFormatOptions()
        {
            var all = new GUIContent[4][];
            for (int p = 0; p < 4; p++)
            {
                var platform = (AtlasPlatform)p;
                IReadOnlyList<AtlasTextureFormat> formats =
                    AtlasPlatformFormats.GetSupportedFormats(platform);
                var options = new GUIContent[formats.Count];
                for (int i = 0; i < formats.Count; i++)
                {
                    options[i] = new GUIContent(
                        AtlasPlatformFormats.GetDisplayName(formats[i]));
                }

                all[p] = options;
            }

            return all;
        }

        // ── Packing guidance blocks ─────────────────────────────────────────────

        /// <summary>
        /// The four guidance blocks are constant per toggle, so they are rendered from pre-built
        /// rich-text content instead of a StringBuilder allocated on every OnGUI pass.
        /// </summary>
        public static readonly GUIContent RotationGuidance = BuildGuidance(
            "Atlas Rotation",
            "Lets sprites rotate 90° during packing so the atlas fits more sprites per page.",
            "Filtered UI (Bilinear / Trilinear) — the standard case.",
            "Pixel art (rotated packing breaks point-filtered pixels) and "
            + "shaders whose visuals depend on a fixed orientation.",
            "Pixel-art rules never rotate, not even with the override set to Enabled: "
            + "rotated packing samples at non-integer texels and visibly damages the art, "
            + "so it is a hard block rather than a default. On every other rule, Enabled "
            + "turns rotation on and Disabled turns it off.");

        public static readonly GUIContent TightPackingGuidance = BuildGuidance(
            "Tight Packing",
            "Denser atlas — polygonal cells waste less space than bounding rectangles.",
            "Standard for UI; almost always the right choice.",
            "When you want a more predictable, rectangular layout (easier to reason "
            + "about, slightly faster atlas build).",
            null);

        public static readonly GUIContent AlphaDilationGuidance = BuildGuidance(
            "Alpha Dilation",
            "Extends each sprite's edge colour into the padding around it — filtered and "
            + "compressed atlases (ASTC, ETC2, PVRTC) stop producing seams at sprite edges.",
            "Filtered and compressed atlases. The standard anti-seam measure.",
            "Point-filtered pixel art (no filtering samples the padding — "
            + "dilation is a no-op there) or art that already carries its own "
            + "baked transparent border.",
            "Edge pollution: dilation amplifies whatever is at the sprite's edge. "
            + "A dirty edge (JPEG ringing, a stray semi-transparent fringe, an "
            + "artist-added border that doesn't match) gets baked into the padding "
            + "and can show as a visible halo. Pixel-art rules auto-disable when "
            + "the per-rule override stays Inherit.");

        public static readonly GUIContent IncludeInBuildGuidance = BuildGuidance(
            "Include In Build",
            "Sprites referenced by installer-baked content (AOT scenes, boot UI) use "
            + "this atlas — one texture, fewer draw calls. The safe baseline for monolithic "
            + "builds.",
            "Monolithic builds with no asset-package hot update — the common Unity project.",
            "Hot-updated projects whose atlases ship in AssetPackage / Addressables / "
            + "YooAsset / xasset: baking the atlas into the installer duplicates it. "
            + "Sprites still render via their individual source textures — no "
            + "missing textures, but the atlas's draw-call savings are lost in the "
            + "installer.",
            "Mixed AOT + AssetPackage: keep this Off globally and Force On only the bootstrap "
            + "rules. Validation lists every rule whose sprites are referenced by "
            + "installer-baked content while resolving Off — that is the rule to force on.");

        public static readonly GUIContent RuleOverridesIntro = new GUIContent(
            "Inherit uses the parent value: the project's global setting (Include In Build, "
            + "Alpha Dilation, Atlas Rotation) or this rule's shared Atlas Max (the size "
            + "popups). Force On / Force Off locks this rule regardless of the parent.");

        private static GUIContent BuildGuidance(
            string toggleName,
            string on,
            string whenToOn,
            string whenToOff,
            string tip)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>On:</b> ").Append(on);
            sb.Append('\n').Append("<b>When On:</b> ").Append(whenToOn);
            sb.Append('\n').Append("<b>When Off:</b> ").Append(whenToOff);
            if (!string.IsNullOrEmpty(tip))
            {
                sb.Append('\n').Append("<b>Tip:</b> ").Append(tip);
            }

            return new GUIContent(sb.ToString());
        }

        // ── Styles and layout options ───────────────────────────────────────────

        private static GUIStyle s_richHelpBox;

        /// <summary>
        /// HelpBox visuals with rich text enabled. EditorStyles.helpBox has
        /// <c>richText = false</c> in Unity 2022, so HelpBox renders <c>&lt;b&gt;</c> markup
        /// literally; this derived style keeps the border and padding but renders markup.
        /// </summary>
        public static GUIStyle RichHelpBoxStyle =>
            s_richHelpBox ??= new GUIStyle(EditorStyles.helpBox)
            {
                richText = true,
                wordWrap = true,
            };

        /// <summary>
        /// Cached layout option: most GUILayout option helpers allocate a wrapper per call, so
        /// hot paths pass this cached instance instead.
        /// </summary>
        public static readonly GUILayoutOption[] ExpandWidth =
        {
            GUILayout.ExpandWidth(true),
        };

        /// <summary>
        /// Cached "N SUFFIX" badge text. Section headers render these on every pass; the count
        /// changes rarely, so the string is rebuilt only when the count actually changes.
        /// </summary>
        public static string CountText(int count, string singular, string plural)
        {
            return count + " " + (count == 1 ? singular : plural);
        }
    }

    /// <summary>
    /// Per-call-site memo for a "N singular/plural" badge: rebuilds the string only when the count
    /// changes. Struct fields live in the window, so this allocates nothing per frame.
    /// </summary>
    public struct UiCountText
    {
        private readonly string _singular;
        private readonly string _plural;
        private int _count;
        private string _text;

        public UiCountText(string singular, string plural)
        {
            _singular = singular;
            _plural = plural;
            _count = int.MinValue;
            _text = string.Empty;
        }

        public string Get(int count)
        {
            if (count != _count || _text == null)
            {
                _count = count;
                _text = count + " " + (count == 1 ? _singular : _plural);
            }

            return _text;
        }
    }
}
