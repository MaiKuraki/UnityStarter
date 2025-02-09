using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BuildReportTool
{
	[System.Serializable, System.Xml.Serialization.XmlRoot("TextureData")]
	public class TextureData : BuildReportTool.IDataFile
	{
		// ==================================================================================

		/// <summary>
		/// Name of project folder.
		/// Included as part of the filename when saved.
		/// </summary>
		public string ProjectName;

		/// <summary>
		/// Type of build that the project was configured to, at the time that TextureData was collected.
		/// Included as part of the filename when saved.
		/// </summary>
		public string BuildType;

		/// <summary>
		/// When TextureData was collected.
		/// Included as part of the filename when saved.
		/// </summary>
		public System.DateTime TimeGot;

		public string GetDefaultFilename()
		{
			return BuildReportTool.Util.GetTextureDataDefaultFilename(ProjectName, BuildType, TimeGot);
		}

		public string GetAccompanyingBuildReportFilename()
		{
			return BuildReportTool.Util.GetBuildInfoDefaultFilename(ProjectName, BuildType, TimeGot);
		}

		/// <summary>
		/// Full path where this TextureData is saved in the local storage.
		/// </summary>
		string _savedPath;

		/// <inheritdoc cref="_savedPath"/>
		public string SavedPath
		{
			get { return _savedPath; }
		}

		public void SetSavedPath(string val)
		{
			_savedPath = val.Replace("\\", "/");
		}

		public bool HasContents
		{
			get { return _textureData.Count > 0; }
		}

		// ==================================================================================

		public enum DataId
		{
			None,
			TextureType,
			IsSRGB,
			AlphaSource,
			AlphaIsTransparency,
			IgnorePngGamma,

			NPotScale,
			IsReadable,
			MipMapGenerated,
			MipMapFilter,
			StreamingMipMaps,
			BorderMipMaps,
			PreserveCoverageMipMaps,
			FadeOutMipMaps,

			SpriteImportMode,
			SpritePackingTag,
			SpritePixelsPerUnit,
			QualifiesForSpritePacking,

			WrapMode,
			WrapModeU,
			WrapModeV,
			WrapModeW,
			FilterMode,
			AnisoLevel,

			MaxTextureSize,
			TextureResizeAlgorithm,
			TextureFormat,
			CompressionType,
			CompressionIsCrunched,
			CompressionQuality,

			ImportedWidthAndHeight,
			RealWidthAndHeight,
		}

		public const string TOOLTIP_TEXT_TEXTURE_TYPE = @"<b><color=white>Texture Type</color></b>

Broad category whether this image is used as a Texture, GUI, Sprite, Cursor, Lightmap, etc.";

		public const string TOOLTIP_TEXT_IS_RGB = @"<b><color=white>Is sRGB (Color Texture)</color></b>

Is this texture for color data?";

		public const string TOOLTIP_TEXT_ALPHA_SOURCE = @"<b><color=white>Alpha Source</color></b>

How the Alpha Channel is generated, if any. Whether it uses the actual Alpha Channel from the source file, or if it's generated from the image's grayscale.";

		public const string TOOLTIP_TEXT_ALPHA_IS_TRANSPARENCY = @"<b><color=white>Alpha is Transparency</color></b>

If the image's Alpha Channel is used for transparency or not. This ensures the RGB channels are dilated to avoid filtering artifacts on the edges.";

#if UNITY_2020_1_OR_NEWER
		public const string TOOLTIP_TEXT_IGNORE_PNG_GAMMA = @"<b><color=white>Ignore PNG Gamma</color></b>

Whether to ignore the Gamma attribute in the PNG file or not (only relevant for PNG files).";
#else
		public const string TOOLTIP_TEXT_IGNORE_PNG_GAMMA = @"<b><color=white>Ignore PNG Gamma</color></b>

Whether to ignore the Gamma attribute in the PNG file or not (only relevant for PNG files).

Only in Unity 2020.1+";
#endif

		public const string TOOLTIP_TEXT_NPOT_SCALE = @"<b><color=white>Non-Power of Two Scale</color></b>

How the image was resized, if width and/or height isn't a power of two. Possible values are: None, ToSmaller, ToLarger, or ToNearest.

Width and height are resized independently, except for certain file formats (like PVRTC) where width and height values need to match (square dimensions).";

		public const string TOOLTIP_TEXT_IS_READABLE = @"<b><color=white>Read/Write Enabled</color></b>

Whether the image's pixel data is accessible from scripts.";

		public const string TOOLTIP_TEXT_MIPMAP_GENERATED = @"<b><color=white>MipMap Generated</color></b>

Whether the image has mip-maps or not.";

		public const string TOOLTIP_TEXT_MIPMAP_FILTER = @"<b><color=white>MipMap Filtering Mode</color></b>

Whether mip-maps are faded out with Box (simple, but can be blurry), or Kaiser (sharper).";

#if UNITY_2018_2_OR_NEWER
		public const string TOOLTIP_TEXT_STREAMING_MIPMAPS = @"<b><color=white>Streaming MipMaps</color></b>

Whether the image is configured (or not) to load larger mip-maps only as they're needed.";
#else
		public const string TOOLTIP_TEXT_STREAMING_MIPMAPS = @"<b><color=white>Streaming MipMaps</color></b>

Whether the image is configured (or not) to load larger mip-maps only as they're needed.

Only in Unity 2018.2+";
#endif

		public const string TOOLTIP_TEXT_BORDER_MIPMAPS = @"<b><color=white>Border MipMaps</color></b>

Whether the texture borders were kept the same when the mip-maps were made. Avoids colors bleeding out to the edge of lower mip levels.";

		public const string TOOLTIP_TEXT_PRESERVE_COVERAGE_MIPMAPS = @"<b><color=white>Preserve Coverage of Alpha MipMaps</color></b>

Whether the shape of alpha channel was preserved for the mip-maps or not.";

		public const string TOOLTIP_TEXT_FADE_MIPMAPS = @"<b><color=white>Fade Out MipMaps</color></b>

Whether mip levels are faded out to grey color.";

		public const string TOOLTIP_TEXT_SPRITE_IMPORT_MODE = @"<b><color=white>Sprite Import Mode</color></b>

Whether image is a single Sprite, a Spritesheet, or is a Sprite with custom polygonal shape.";

		public const string TOOLTIP_TEXT_SPRITE_PACKING_TAG = @"<b><color=white>Sprite Packing Tag</color></b>

Name of the Atlas where this Sprite was packed into.";

		public const string TOOLTIP_TEXT_SPRITE_PIXELS_PER_UNIT = @"<b><color=white>Sprite Pixels-Per-Unit</color></b>

How many pixels in the Sprite take up one world unit of distance in the scene.";

		public const string TOOLTIP_TEXT_SPRITE_QUALIFIES_FOR_PACKING = @"<b><color=white>Sprite Qualifies for Packing</color></b>";

		public const string TOOLTIP_TEXT_WRAP_MODE = @"<b><color=white>Wrap Mode</color></b>

Whether image repeats (tiled), mirrored (like tiling but is mirrored), clamped (edges are stretched), etc.";

		public const string TOOLTIP_TEXT_WRAP_MODE_U = @"<b><color=white>Wrap Mode U</color></b>

Whether image repeats (tiled), mirrored (like tiling but is mirrored), clamped (edges are stretched), etc. in the U-axis (UV space).";

		public const string TOOLTIP_TEXT_WRAP_MODE_V = @"<b><color=white>Wrap Mode V</color></b>

Whether image repeats (tiled), mirrored (like tiling but is mirrored), clamped (edges are stretched), etc. in the V-axis (UV space).";

		public const string TOOLTIP_TEXT_WRAP_MODE_W = @"<b><color=white>Wrap Mode W</color></b>

Whether image repeats (tiled), mirrored (like tiling but is mirrored), clamped (edges are stretched), etc. in the W-axis (UVW space).";

		public const string TOOLTIP_TEXT_FILTER_MODE = @"<b><color=white>Filter Mode</color></b>

How filtering is handled when image gets stretched by 3d transformations.

Can be Point (pixels become blocky when enlarged), Bilinear (texture samples are averaged), or Trilinear (texture samples are averaged, and also blended between mip-map levels).";

		public const string TOOLTIP_TEXT_ANISO_LEVEL = @"<b><color=white>Anisotropic Filtering Level</color></b>

How much visual quality is preserved when texture is viewed from a grazing angle (0 means disabled).";

		public const string TOOLTIP_TEXT_MAX_SIZE = @"<b><color=white>Max Texture Size</color></b>

Limit imposed on the width and height of the image.";

		public const string TOOLTIP_TEXT_RESIZE_ALGO = @"<b><color=white>Resize Algorithm</color></b>

If image was downscaled due to <b><color=white>Max Texture Size</color></b>, this determines the quality of downscaling.";

		public const string TOOLTIP_TEXT_FORMAT = @"<b><color=white>Texture Format</color></b>

Format that the image was converted into (DXT, PVRTC, RGBA32 uncompressed, etc.).

These are formats for runtime use, and are a different set of formats from the image's original file type.";

		public const string TOOLTIP_TEXT_COMPRESSION_TYPE = @"<b><color=white>Compression Type</color></b>

Whether the image, upon being converted for its <b><color=white>Texture Format</color></b>, was compressed, and how much compression was done (low, standard, high).";

		public const string TOOLTIP_TEXT_COMPRESSION_CRUNCHED = @"<b><color=white>Compression Crunched</color></b>

Whether crunch compression was used on the image or not. Crunch is lossy, and only reduces the size taken up on disk.";

		public const string TOOLTIP_TEXT_COMPRESSION_QUALITY = @"<b><color=white>Compression Quality</color></b>

Level of quality upon compressing.
100 = Highest quality possible, but also biggest file size.
0 = Lowest quality, but also smallest file size.";

		public const string TOOLTIP_TEXT_IMPORTED_WIDTH_AND_HEIGHT = @"<b><color=white>Imported Width and Height</color></b>

The width and height of the image after it was resized by the <b><color=white>Max Texture Size</color></b> limit and <b><color=white>Non-Power of Two Scale</color></b> restrictions.";

		public const string TOOLTIP_TEXT_REAL_WIDTH_AND_HEIGHT = @"<b><color=white>Source Width and Height</color></b>

The original width and height of the image <i>before</i> it was resized by the <b><color=white>Max Texture Size</color></b> limit and <b><color=white>Non-Power of Two Scale</color></b> restrictions.";

		public static string GetTooltipTextFromId(DataId textureDataId)
		{
			switch (textureDataId)
			{
				case DataId.TextureType:
					return TOOLTIP_TEXT_TEXTURE_TYPE;
				case DataId.IsSRGB:
					return TOOLTIP_TEXT_IS_RGB;
				case DataId.AlphaSource:
					return TOOLTIP_TEXT_ALPHA_SOURCE;
				case DataId.AlphaIsTransparency:
					return TOOLTIP_TEXT_ALPHA_IS_TRANSPARENCY;
				case DataId.IgnorePngGamma:
					return TOOLTIP_TEXT_IGNORE_PNG_GAMMA;
				case DataId.NPotScale:
					return TOOLTIP_TEXT_NPOT_SCALE;
				case DataId.IsReadable:
					return TOOLTIP_TEXT_IS_READABLE;
				// -----------------------------------------------
				case DataId.MipMapGenerated:
					return TOOLTIP_TEXT_MIPMAP_GENERATED;
				case DataId.MipMapFilter:
					return TOOLTIP_TEXT_MIPMAP_FILTER;
				case DataId.StreamingMipMaps:
					return TOOLTIP_TEXT_STREAMING_MIPMAPS;
				case DataId.BorderMipMaps:
					return TOOLTIP_TEXT_BORDER_MIPMAPS;
				case DataId.PreserveCoverageMipMaps:
					return TOOLTIP_TEXT_PRESERVE_COVERAGE_MIPMAPS;
				case DataId.FadeOutMipMaps:
					return TOOLTIP_TEXT_FADE_MIPMAPS;
				// -----------------------------------------------
				case DataId.SpriteImportMode:
					return TOOLTIP_TEXT_SPRITE_IMPORT_MODE;
				case DataId.SpritePackingTag:
					return TOOLTIP_TEXT_SPRITE_PACKING_TAG;
				case DataId.SpritePixelsPerUnit:
					return TOOLTIP_TEXT_SPRITE_PIXELS_PER_UNIT;
				case DataId.QualifiesForSpritePacking:
					return TOOLTIP_TEXT_SPRITE_QUALIFIES_FOR_PACKING;
				// -----------------------------------------------
				case DataId.WrapMode:
					return TOOLTIP_TEXT_WRAP_MODE;
				case DataId.WrapModeU:
					return TOOLTIP_TEXT_WRAP_MODE_U;
				case DataId.WrapModeV:
					return TOOLTIP_TEXT_WRAP_MODE_V;
				case DataId.WrapModeW:
					return TOOLTIP_TEXT_WRAP_MODE_W;
				case DataId.FilterMode:
					return TOOLTIP_TEXT_FILTER_MODE;
				case DataId.AnisoLevel:
					return TOOLTIP_TEXT_ANISO_LEVEL;
				// -----------------------------------------------
				case DataId.MaxTextureSize:
					return TOOLTIP_TEXT_MAX_SIZE;
				case DataId.TextureResizeAlgorithm:
					return TOOLTIP_TEXT_RESIZE_ALGO;
				case DataId.TextureFormat:
					return TOOLTIP_TEXT_FORMAT;
				case DataId.CompressionType:
					return TOOLTIP_TEXT_COMPRESSION_TYPE;
				case DataId.CompressionIsCrunched:
					return TOOLTIP_TEXT_COMPRESSION_CRUNCHED;
				case DataId.CompressionQuality:
					return TOOLTIP_TEXT_COMPRESSION_QUALITY;
				// -----------------------------------------------
				case DataId.ImportedWidthAndHeight:
					return TOOLTIP_TEXT_IMPORTED_WIDTH_AND_HEIGHT;
				case DataId.RealWidthAndHeight:
					return TOOLTIP_TEXT_REAL_WIDTH_AND_HEIGHT;
				// -----------------------------------------------
				default:
					return null;
			}
		}

		public const string NPOT_SCALE_NONE_NOT_POT = "None (Not Power of 2)";
		public const string NPOT_SCALE_NONE_IS_POT = "None (Is Power of 2)";

		public struct Entry
		{
			/// <summary>
			/// Broad category whether this image is used as a Texture, GUI, Sprite, Cursor, Lightmap, etc.
			/// Maps to <see cref="UnityEditor.TextureImporter.textureType"/>.
			/// <inheritdoc cref="UnityEditor.TextureImporter.textureType"/>
			/// See <see cref="UnityEditor.TextureImporterType"/> for possible values.
			/// </summary>
			public string TextureType;

			/// <summary>
			/// true: sRGB false: Linear.
			/// For Unity 5.4 and below: maps to UnityEditor.TextureImporter.linearTexture (inverted value).
			/// For Unity 5.5 and above: maps to <see cref="UnityEditor.TextureImporter.sRGBTexture"/>.
			/// <inheritdoc cref="UnityEditor.TextureImporter.sRGBTexture"/>
			/// </summary>
			public bool IsSRGB;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.alphaSource"/>.
			/// <inheritdoc cref="UnityEditor.TextureImporter.alphaSource"/>
			/// See <see cref="UnityEditor.TextureImporterAlphaSource"/> for possible values.
			/// </summary>
			public string AlphaSource;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.alphaIsTransparency"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.alphaIsTransparency"/>
			/// </summary>
			public bool AlphaIsTransparency;

			/// <summary>
			/// Added in Unity 2020.1.
			/// Maps to <see cref="UnityEditor.TextureImporter.ignorePngGamma"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.ignorePngGamma"/>
			/// </summary>
			public bool IgnorePngGamma;

			// ----------------------------------------

			/// <summary>
			/// How the image is resized if height/width isn't a power of two, if at all.
			/// (None, ToNearest, ToLarger, or ToSmaller).
			/// Maps to <see cref="UnityEditor.TextureImporter.npotScale"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.npotScale"/>
			/// See <see cref="UnityEditor.TextureImporterNPOTScale"/> for possible values.
			/// </summary>
			public string NPotScale;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.isReadable"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.isReadable"/>
			/// </summary>
			public bool IsReadable;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.mipmapEnabled"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.mipmapEnabled"/>
			/// </summary>
			public bool MipMapGenerated;

			/// <summary>
			/// Added in Unity 2018.2.
			/// Maps to <see cref="UnityEditor.TextureImporter.streamingMipmaps"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.streamingMipmaps"/>
			/// </summary>
			public bool StreamingMipMaps;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.borderMipmap"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.borderMipmap"/>
			/// </summary>
			public bool BorderMipMaps;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.mipmapFilter"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.mipmapFilter"/>
			/// See <see cref="UnityEditor.TextureImporterMipFilter"/> for possible values.
			/// </summary>
			public string MipMapFilter;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.mipMapsPreserveCoverage"/>
			/// Added in Unity 2017.1.
			/// <inheritdoc cref="UnityEditor.TextureImporter.mipMapsPreserveCoverage"/>
			/// </summary>
			public bool PreserveCoverageMipMaps;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.fadeout"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.fadeout"/>
			/// </summary>
			public bool FadeOutMipMaps;

			// ----------------------------------------

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.spriteImportMode"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.spriteImportMode"/>
			/// See <see cref="UnityEditor.SpriteImportMode"/> for possible values.
			/// </summary>
			public string SpriteImportMode;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.spritePackingTag"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.spritePackingTag"/>
			/// </summary>
			public string SpritePackingTag;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.spritePixelsPerUnit"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.spritePixelsPerUnit"/>
			/// </summary>
			public float SpritePixelsPerUnit;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.qualifiesForSpritePacking"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.qualifiesForSpritePacking"/>
			/// </summary>
			public bool QualifiesForSpritePacking;

			// ----------------------------------------

			/// <summary>
			/// Whether repeated or clamped.
			/// Maps to <see cref="UnityEditor.TextureImporter.wrapMode"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.wrapMode"/>
			/// See <see cref="TextureWrapMode"/> for possible values.
			/// </summary>
			public string WrapMode;

			/// <summary>
			/// Whether repeated or clamped in the U axis.
			/// Maps to <see cref="UnityEditor.TextureImporter.wrapModeU"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.wrapModeU"/>
			/// See <see cref="TextureWrapMode"/> for possible values.
			/// </summary>
			public string WrapModeU;

			/// <summary>
			/// Whether repeated or clamped in the V axis.
			/// Maps to <see cref="UnityEditor.TextureImporter.wrapModeV"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.wrapModeV"/>
			/// See <see cref="TextureWrapMode"/> for possible values.
			/// </summary>
			public string WrapModeV;

			/// <summary>
			/// Whether repeated or clamped in the W axis.
			/// Maps to <see cref="UnityEditor.TextureImporter.wrapModeW"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.wrapModeW"/>
			/// See <see cref="TextureWrapMode"/> for possible values.
			/// </summary>
			public string WrapModeW;

			/// <summary>
			/// Point, Bilinear or Trilinear. Maps to <see cref="UnityEditor.TextureImporter.filterMode"/>.
			/// <inheritdoc cref="UnityEditor.TextureImporter.filterMode"/>
			/// See <see cref="UnityEngine.FilterMode"/> for possible values.
			/// </summary>
			public string FilterMode;

			/// <summary>
			/// Anisotropic filtering level. Maps to <see cref="UnityEditor.TextureImporter.anisoLevel"/>.
			/// <inheritdoc cref="UnityEditor.TextureImporter.anisoLevel"/>
			/// </summary>
			public int AnisoLevel;

			// ----------------------------------------

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.maxTextureSize"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.maxTextureSize"/>
			/// </summary>
			public int MaxTextureSize;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporterPlatformSettings.resizeAlgorithm"/>
			/// <inheritdoc cref="UnityEditor.TextureImporterPlatformSettings.resizeAlgorithm"/>
			/// </summary>
			public string TextureResizeAlgorithm;

			/// <summary>
			/// Image format upon import (DXT, PVRTC, RGBA32 uncompressed, etc.).
			/// Maps to <see cref="UnityEditor.TextureImporterPlatformSettings.format"/>,
			/// but if that value is <see cref="UnityEditor.TextureImporterFormat.Automatic"/>,
			/// then it uses the value of <see cref="UnityEngine.Texture2D.format"/>.
			/// <inheritdoc cref="UnityEditor.TextureImporter.textureFormat"/>
			/// See <see cref="UnityEditor.TextureImporterFormat"/> (https://docs.unity3d.com/ScriptReference/TextureImporterFormat.html) and
			/// <see cref="UnityEngine.TextureFormat"/> (https://docs.unity3d.com/ScriptReference/TextureFormat.html) for possible values.
			/// </summary>
			public string TextureFormat;

			/// <summary>
			/// (only in Unity 5.5+)
			/// Whether it is low, medium, high compression, or none at all.
			/// Maps to <see cref="UnityEditor.TextureImporterPlatformSettings.textureCompression"/>.
			/// <inheritdoc cref="UnityEditor.TextureImporterPlatformSettings.textureCompression"/>
			/// See <see cref="UnityEditor.TextureImporterCompression"/> for possible values.
			/// </summary>
			public string CompressionType;

			/// <summary>
			/// (only in Unity 5.5+)
			/// Maps to <see cref="UnityEditor.TextureImporter.crunchedCompression"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.crunchedCompression"/>
			/// </summary>
			public bool CompressionIsCrunched;

			/// <summary>
			/// Maps to <see cref="UnityEditor.TextureImporter.compressionQuality"/>. Goes from 0 to 100.
			/// <inheritdoc cref="UnityEditor.TextureImporter.compressionQuality"/>
			/// </summary>
			public int CompressionQuality;

			// ----------------------------------------

			public bool PlatformSettingsOverriden;

			/// <summary>
			/// Like <see cref="MaxTextureSize"/> but if the image's platform override was set, this is the overriding value.
			/// Maps to <see cref="UnityEditor.TextureImporterPlatformSettings.maxTextureSize"/>
			/// <inheritdoc cref="UnityEditor.TextureImporterPlatformSettings.maxTextureSize"/>
			/// </summary>
			public int OverridingMaxTextureSize;

			/// <summary>
			/// Like <see cref="TextureResizeAlgorithm"/> but if the image's platform override was set, this is the overriding value.
			/// Added in Unity 2017.2.
			/// Maps to <see cref="UnityEditor.TextureImporterPlatformSettings.resizeAlgorithm"/>
			/// <inheritdoc cref="UnityEditor.TextureImporterPlatformSettings.resizeAlgorithm"/>
			/// </summary>
			public string OverridingTextureResizeAlgorithm;

			/// <summary>
			/// Like <see cref="TextureFormat"/> but if the image's platform override was set, this is the overriding value.
			/// Maps to <see cref="UnityEditor.TextureImporterPlatformSettings.format"/>
			/// <inheritdoc cref="UnityEditor.TextureImporterPlatformSettings.format"/>
			/// See <see cref="UnityEditor.TextureImporterFormat"/> for possible values.
			/// </summary>
			public string OverridingTextureFormat;

			/// <summary>
			/// Like <see cref="CompressionType"/> but if the image's platform override was set, this is the overriding value.
			/// Maps to <see cref="UnityEditor.TextureImporterPlatformSettings.textureCompression"/>.
			/// <inheritdoc cref="UnityEditor.TextureImporterPlatformSettings.textureCompression"/>
			/// See <see cref="UnityEditor.TextureImporterCompression"/> for possible values.
			/// </summary>
			public string OverridingCompressionType;

			/// <summary>
			/// Like <see cref="CompressionIsCrunched"/> but if the image's platform override was set, this is the overriding value.
			/// Maps to <see cref="UnityEditor.TextureImporter.crunchedCompression"/>
			/// <inheritdoc cref="UnityEditor.TextureImporter.crunchedCompression"/>
			/// </summary>
			public bool OverridingCompressionIsCrunched;

			/// <summary>
			/// Like <see cref="CompressionQuality"/> but if the image's platform override was set, this is the overriding value.
			/// Maps to <see cref="UnityEditor.TextureImporter.compressionQuality"/>. Goes from 0 to 100.
			/// <inheritdoc cref="UnityEditor.TextureImporter.compressionQuality"/>
			/// </summary>
			public int OverridingCompressionQuality;

			// ----------------------------------------

			/// <summary>
			/// Image width, but if <see cref="UnityEditor.TextureImporter.npotScale"/>
			/// and/or <see cref="UnityEditor.TextureImporter.maxTextureSize"/> is used,
			/// this will be the value that it was resized to.
			/// </summary>
			public int ImportedWidth;

			/// <summary>
			/// Image height, but if <see cref="UnityEditor.TextureImporter.npotScale"/>
			/// and/or <see cref="UnityEditor.TextureImporter.maxTextureSize"/> is used,
			/// this will be the value that it was resized to.
			/// </summary>
			public int ImportedHeight;

			/// <summary>
			/// Actual image width, before the <see cref="UnityEditor.TextureImporter.npotScale"/>
			/// and/or <see cref="UnityEditor.TextureImporter.maxTextureSize"/> settings resized it.
			/// </summary>
			public int RealWidth;

			/// <summary>
			/// Actual image height, before the <see cref="UnityEditor.TextureImporter.npotScale"/>
			/// and/or <see cref="UnityEditor.TextureImporter.maxTextureSize"/> settings resized it.
			/// </summary>
			public int RealHeight;

			// ----------------------------------------

			/// <summary>
			/// Either <see cref="MaxTextureSize"/> or <see cref="OverridingMaxTextureSize"/>,
			/// depending on whether <see cref="PlatformSettingsOverriden"/> is true or not.
			/// </summary>
			int _shownMaxTextureSize;

			/// <summary>
			/// Either <see cref="MaxTextureSize"/> or <see cref="OverridingMaxTextureSize"/>,
			/// depending on whether <see cref="PlatformSettingsOverriden"/> is true or not.
			/// This value is a string, for directly displaying in the GUI.
			/// If value is using <see cref="OverridingMaxTextureSize"/>
			/// and it is really different from <see cref="MaxTextureSize"/>,
			/// it will have an asterisk at the end.
			/// </summary>
			string _shownMaxTextureSizeLabel;

			string _overridingMaxTextureSizeMessage;

			// ---------------

			/// <summary>
			/// Either <see cref="TextureResizeAlgorithm"/> or <see cref="OverridingTextureResizeAlgorithm"/>,
			/// depending on whether <see cref="PlatformSettingsOverriden"/> is true or not.
			/// This value is a string, for directly displaying in the GUI.
			/// If value is using <see cref="OverridingTextureResizeAlgorithm"/>
			/// and it is really different from <see cref="TextureResizeAlgorithm"/>,
			/// it will have an asterisk at the end.
			/// </summary>
			string _shownTextureResizeAlgorithm;

			string _overridingTextureResizeAlgorithmMessage;

			// ---------------

			/// <summary>
			/// Either <see cref="TextureFormat"/> or <see cref="OverridingTextureFormat"/>,
			/// depending on whether <see cref="PlatformSettingsOverriden"/> is true or not.
			/// This value is a string, for directly displaying in the GUI.
			/// If value is using <see cref="OverridingTextureFormat"/>
			/// and it is really different from <see cref="TextureFormat"/>,
			/// it will have an asterisk at the end.
			/// </summary>
			string _shownTextureFormat;

			string _overridingTextureFormatMessage;

			// ---------------

			/// <summary>
			/// Either <see cref="CompressionType"/> or <see cref="OverridingCompressionType"/>,
			/// depending on whether <see cref="PlatformSettingsOverriden"/> is true or not.
			/// This value is a string, for directly displaying in the GUI.
			/// If value is using <see cref="OverridingCompressionType"/>
			/// and it is really different from <see cref="CompressionType"/>,
			/// it will have an asterisk at the end.
			/// </summary>
			string _shownCompressionType;

			string _overridingCompressionTypeMessage;

			// ---------------

			/// <summary>
			/// Either <see cref="CompressionIsCrunched"/> or <see cref="OverridingCompressionIsCrunched"/>,
			/// depending on whether <see cref="PlatformSettingsOverriden"/> is true or not.
			/// </summary>
			bool _shownCompressionIsCrunched;

			/// <summary>
			/// Either <see cref="CompressionIsCrunched"/> or <see cref="OverridingCompressionIsCrunched"/>,
			/// depending on whether <see cref="PlatformSettingsOverriden"/> is true or not.
			/// This value is a string, for directly displaying in the GUI.
			/// If value is using <see cref="OverridingCompressionIsCrunched"/>
			/// and it is really different from <see cref="CompressionIsCrunched"/>,
			/// it will have an asterisk at the end.
			/// </summary>
			string _shownCompressionIsCrunchedLabel;

			string _overridingCompressionIsCrunchedMessage;

			// ---------------

			/// <summary>
			/// Either <see cref="CompressionQuality"/> or <see cref="OverridingCompressionQuality"/>,
			/// depending on whether <see cref="PlatformSettingsOverriden"/> is true or not.
			/// </summary>
			int _shownCompressionQuality;

			/// <summary>
			/// Either <see cref="CompressionQuality"/> or <see cref="OverridingCompressionQuality"/>,
			/// depending on whether <see cref="PlatformSettingsOverriden"/> is true or not.
			/// This value is a string, for directly displaying in the GUI.
			/// If value is using <see cref="OverridingCompressionQuality"/>
			/// and it is really different from <see cref="CompressionQuality"/>,
			/// it will have an asterisk at the end.
			/// </summary>
			string _shownCompressionQualityLabel;

			string _overridingCompressionQualityMessage;

			// ----------------------------------------

			public int GetShownMaxTextureSize()
			{
				return _shownMaxTextureSize;
			}

			public string GetShownTextureResizeAlgorithm()
			{
				return _shownTextureResizeAlgorithm;
			}

			public string GetShownTextureFormat()
			{
				return _shownTextureFormat;
			}

			public string GetShownCompressionType()
			{
				return _shownCompressionType;
			}

			public bool GetShownCompressionIsCrunched()
			{
				return _shownCompressionIsCrunched;
			}

			public int GetShownCompressionQuality()
			{
				return _shownCompressionQuality;
			}

			public int GetImportedPixelCount()
			{
				return ImportedWidth * ImportedHeight;
			}

			public int GetRealPixelCount()
			{
				return RealWidth * RealHeight;
			}

			// ==================================================================================

			public Entry(string n = null)
			{
				TextureType = null;
				IsSRGB = false;
				AlphaSource = null;
				AlphaIsTransparency = false;
				IgnorePngGamma = false;

				NPotScale = null;
				IsReadable = false;
				MipMapGenerated = false;
				MipMapFilter = null;
				StreamingMipMaps = false;
				BorderMipMaps = false;
				PreserveCoverageMipMaps = false;
				FadeOutMipMaps = false;

				SpriteImportMode = null;
				SpritePackingTag = null;
				SpritePixelsPerUnit = 0;
				QualifiesForSpritePacking = false;

				WrapMode = null;
				WrapModeU = null;
				WrapModeV = null;
				WrapModeW = null;
				FilterMode = null;
				AnisoLevel = 0;

				MaxTextureSize = 0;
				TextureResizeAlgorithm = null;
				TextureFormat = null;
				CompressionType = null;
				CompressionIsCrunched = false;
				CompressionQuality = 0;

				PlatformSettingsOverriden = false;
				OverridingMaxTextureSize = 0;
				OverridingTextureResizeAlgorithm = null;
				OverridingTextureFormat = null;
				OverridingCompressionType = null;
				OverridingCompressionIsCrunched = false;
				OverridingCompressionQuality = 0;

				ImportedWidth = 0;
				ImportedHeight = 0;
				RealWidth = 0;
				RealHeight = 0;

				_shownMaxTextureSize = 0;
				_shownMaxTextureSizeLabel = null;
				_shownTextureResizeAlgorithm = null;
				_shownTextureFormat = null;
				_shownCompressionType = null;
				_shownCompressionIsCrunched = false;
				_shownCompressionIsCrunchedLabel = null;
				_shownCompressionQuality = 0;
				_shownCompressionQualityLabel = null;

				_overridingMaxTextureSizeMessage = null;
				_overridingTextureResizeAlgorithmMessage = null;
				_overridingTextureFormatMessage = null;
				_overridingCompressionTypeMessage = null;
				_overridingCompressionIsCrunchedMessage = null;
				_overridingCompressionQualityMessage = null;
			}

			public void UpdateShownSettings(string platformName)
			{
				if (!PlatformSettingsOverriden)
				{
					_shownMaxTextureSize = MaxTextureSize;
					_shownMaxTextureSizeLabel = MaxTextureSize.ToString();
					_overridingMaxTextureSizeMessage = null;

					_shownTextureResizeAlgorithm = TextureResizeAlgorithm;
					_overridingTextureResizeAlgorithmMessage = null;

					_shownTextureFormat = TextureFormat;
					_overridingTextureFormatMessage = null;

					_shownCompressionType = CompressionType;
					_overridingCompressionTypeMessage = null;

					_shownCompressionIsCrunched = CompressionIsCrunched;
					_shownCompressionIsCrunchedLabel = CompressionIsCrunched.ToYesNo();
					_overridingCompressionIsCrunchedMessage = null;

					_shownCompressionQuality = CompressionQuality;
					_shownCompressionQualityLabel = CompressionQuality.ToString();
					_overridingCompressionQualityMessage = null;

					return;
				}

				if (OverridingMaxTextureSize != MaxTextureSize)
				{
					_shownMaxTextureSize = OverridingMaxTextureSize;
					_shownMaxTextureSizeLabel = string.Format("{0}*", OverridingMaxTextureSize.ToString());

					if (string.IsNullOrEmpty(platformName))
					{
						_overridingMaxTextureSizeMessage =
							string.Format("<b><color=white>Max Texture Size</color></b> has been overriden from <b><color=white>{0}</color></b> to <b><color=white>{1}</color></b>",
								MaxTextureSize.ToString(), OverridingMaxTextureSize.ToString());
					}
					else
					{
						_overridingMaxTextureSizeMessage =
							string.Format("<b><color=white>Max Texture Size</color></b> in <b><color=white>{0}</color></b> has been overriden from <b><color=white>{1}</color></b> to <b><color=white>{2}</color></b>",
								platformName, MaxTextureSize.ToString(), OverridingMaxTextureSize.ToString());
					}
				}
				else
				{
					_shownMaxTextureSize = MaxTextureSize;
					_shownMaxTextureSizeLabel = MaxTextureSize.ToString();
					_overridingMaxTextureSizeMessage = null;
				}

				if (!string.IsNullOrEmpty(OverridingTextureResizeAlgorithm) && OverridingTextureResizeAlgorithm != TextureResizeAlgorithm)
				{
					_shownTextureResizeAlgorithm = string.Format("{0}*", OverridingTextureResizeAlgorithm);

					if (string.IsNullOrEmpty(platformName))
					{
						_overridingTextureResizeAlgorithmMessage =
							string.Format("<b><color=white>Texture Resize Algorithm</color></b> has been overriden from <b><color=white>{0}</color></b> to <b><color=white>{1}</color></b>",
								TextureResizeAlgorithm, OverridingTextureResizeAlgorithm);
					}
					else
					{
						_overridingTextureResizeAlgorithmMessage =
							string.Format("<b><color=white>Texture Resize Algorithm</color></b> in <b><color=white>{0}</color></b> has been overriden from <b><color=white>{1}</color></b> to <b><color=white>{2}</color></b>",
								platformName, TextureResizeAlgorithm, OverridingTextureResizeAlgorithm);
					}
				}
				else
				{
					_shownTextureResizeAlgorithm = TextureResizeAlgorithm;
					_overridingTextureResizeAlgorithmMessage = null;
				}

				if (!string.IsNullOrEmpty(OverridingTextureFormat) && OverridingTextureFormat != TextureFormat)
				{
					_shownTextureFormat = string.Format("{0}*", OverridingTextureFormat);

					if (string.IsNullOrEmpty(platformName))
					{
						_overridingTextureFormatMessage =
							string.Format("<b><color=white>Texture Format</color></b> has been overriden from <b><color=white>{0}</color></b> to <b><color=white>{1}</color></b>",
								TextureFormat, OverridingTextureFormat);
					}
					else
					{
						_overridingTextureFormatMessage =
							string.Format("<b><color=white>Texture Format</color></b> in <b><color=white>{0}</color></b> has been overriden from <b><color=white>{1}</color></b> to <b><color=white>{2}</color></b>",
								platformName, TextureFormat, OverridingTextureFormat);
					}
				}
				else
				{
					_shownTextureFormat = TextureFormat;
					_overridingTextureFormatMessage = null;
				}

				if (!string.IsNullOrEmpty(OverridingCompressionType) && OverridingCompressionType != CompressionType)
				{
					_shownCompressionType = string.Format("{0}*", OverridingCompressionType);

					if (string.IsNullOrEmpty(platformName))
					{
						_overridingCompressionTypeMessage =
							string.Format("<b><color=white>Compression Type</color></b> has been overriden from <b><color=white>{0}</color></b> to <b><color=white>{1}</color></b>",
								CompressionType, OverridingCompressionType);
					}
					else
					{
						_overridingCompressionTypeMessage =
							string.Format("<b><color=white>Compression Type</color></b> in <b><color=white>{0}</color></b> has been overriden from <b><color=white>{1}</color></b> to <b><color=white>{2}</color></b>",
								platformName, CompressionType, OverridingCompressionType);
					}
				}
				else
				{
					_shownCompressionType = CompressionType;
					_overridingCompressionTypeMessage = null;
				}

				if (OverridingCompressionIsCrunched != CompressionIsCrunched)
				{
					_shownCompressionIsCrunched = OverridingCompressionIsCrunched;
					_shownCompressionIsCrunchedLabel = string.Format("{0}*", OverridingCompressionIsCrunched.ToYesNo());

					if (string.IsNullOrEmpty(platformName))
					{
						_overridingCompressionIsCrunchedMessage =
							string.Format("<b><color=white>Compression is Crunched</color></b> has been overriden from <b><color=white>{0}</color></b> to <b><color=white>{1}</color></b>",
								CompressionIsCrunched.ToYesNo(), OverridingCompressionIsCrunched.ToYesNo());
					}
					else
					{
						_overridingCompressionIsCrunchedMessage =
							string.Format("<b><color=white>Compression is Crunched</color></b> in <b><color=white>{0}</color></b> has been overriden from <b><color=white>{1}</color></b> to <b><color=white>{2}</color></b>",
								platformName, CompressionIsCrunched.ToYesNo(), OverridingCompressionIsCrunched.ToYesNo());
					}
				}
				else
				{
					_shownCompressionIsCrunched = CompressionIsCrunched;
					_shownCompressionIsCrunchedLabel = CompressionIsCrunched.ToYesNo();
					_overridingCompressionIsCrunchedMessage = null;
				}

				if (OverridingCompressionQuality != CompressionQuality)
				{
					_shownCompressionQuality = OverridingCompressionQuality;
					_shownCompressionQualityLabel = string.Format("{0}*", OverridingCompressionQuality.ToString());

					if (string.IsNullOrEmpty(platformName))
					{
						_overridingCompressionQualityMessage =
							string.Format("<b><color=white>Compression Quality</color></b> has been overriden from <b><color=white>{0}</color></b> to <b><color=white>{1}</color></b>",
								CompressionQuality.ToString(), OverridingCompressionQuality.ToString());
					}
					else
					{
						_overridingCompressionQualityMessage =
							string.Format("<b><color=white>Compression Quality</color></b> in <b><color=white>{0}</color></b> has been overriden from <b><color=white>{1}</color></b> to <b><color=white>{2}</color></b>",
								platformName, CompressionQuality.ToString(), OverridingCompressionQuality.ToString());
					}
				}
				else
				{
					_shownCompressionQuality = CompressionQuality;
					_shownCompressionQualityLabel = CompressionQuality.ToString();
					_overridingCompressionQualityMessage = null;
				}
			}

			// ==================================================================================

			public string ToDisplayedValue(DataId dataId)
			{
				switch (dataId)
				{
					case DataId.TextureType:
						return TextureType;
					case DataId.IsSRGB:
						return IsSRGB.ToYesNo();
					case DataId.AlphaSource:
						return AlphaSource;
					case DataId.AlphaIsTransparency:
						return AlphaIsTransparency.ToYesNo();
					case DataId.IgnorePngGamma:
						return IgnorePngGamma.ToYesNo();
					case DataId.NPotScale:
						return NPotScale;
					case DataId.IsReadable:
						return IsReadable.ToYesNo();
					// ------------------------
					case DataId.MipMapGenerated:
						return MipMapGenerated.ToYesNo();
					case DataId.MipMapFilter:
						return MipMapFilter;
					case DataId.StreamingMipMaps:
						return StreamingMipMaps.ToYesNo();
					case DataId.BorderMipMaps:
						return BorderMipMaps.ToYesNo();
					case DataId.PreserveCoverageMipMaps:
						return PreserveCoverageMipMaps.ToYesNo();
					case DataId.FadeOutMipMaps:
						return FadeOutMipMaps.ToYesNo();
					// ------------------------
					case DataId.SpriteImportMode:
						return SpriteImportMode;
					case DataId.QualifiesForSpritePacking:
						return QualifiesForSpritePacking.ToYesNo();
					case DataId.SpritePackingTag:
						return SpritePackingTag;
					case DataId.SpritePixelsPerUnit:
						return SpritePixelsPerUnit.ToString(CultureInfo.InvariantCulture);
					// ------------------------
					case DataId.WrapMode:
						return WrapMode;
					case DataId.WrapModeU:
						return WrapModeU;
					case DataId.WrapModeV:
						return WrapModeV;
					case DataId.WrapModeW:
						return WrapModeW;
					case DataId.FilterMode:
						return FilterMode;
					case DataId.AnisoLevel:
						return AnisoLevel.ToString();
					// ------------------------
					case DataId.MaxTextureSize:
						return _shownMaxTextureSizeLabel;
					case DataId.TextureResizeAlgorithm:
						return _shownTextureResizeAlgorithm;
					case DataId.TextureFormat:
						return _shownTextureFormat;
					case DataId.CompressionType:
						return _shownCompressionType;
					case DataId.CompressionIsCrunched:
						return _shownCompressionIsCrunchedLabel;
					case DataId.CompressionQuality:
						return _shownCompressionQualityLabel;
					// ------------------------
					case DataId.ImportedWidthAndHeight:
						return string.Format("{0}x{1}", ImportedWidth.ToString(), ImportedHeight.ToString());
					case DataId.RealWidthAndHeight:
						return string.Format("{0}x{1}", RealWidth.ToString(), RealHeight.ToString());
					// ------------------------
					default:
						return string.Empty;
				}
			}

			public bool IsImportedWidthAndHeightDifferentFromReal
			{
				get
				{
					return ImportedWidth != RealWidth || ImportedHeight != RealHeight;
				}
			}

			public bool IsOverriden(DataId dataId)
			{
				if (!PlatformSettingsOverriden)
				{
					return false;
				}

				switch (dataId)
				{
					// ------------------------
					case DataId.MaxTextureSize:
						return OverridingMaxTextureSize != MaxTextureSize;
					case DataId.TextureResizeAlgorithm:
						return OverridingTextureResizeAlgorithm != TextureResizeAlgorithm;
					case DataId.TextureFormat:
						return OverridingTextureFormat != TextureFormat;
					case DataId.CompressionType:
						return OverridingCompressionType != CompressionType;
					case DataId.CompressionIsCrunched:
						return OverridingCompressionIsCrunched != CompressionIsCrunched;
					case DataId.CompressionQuality:
						return OverridingCompressionQuality != CompressionQuality;
					// ------------------------
					default:
						return false;
				}
			}

			public string GetOverridingMessage(DataId dataId)
			{
				if (!PlatformSettingsOverriden)
				{
					return null;
				}

				switch (dataId)
				{
					// ------------------------
					case DataId.MaxTextureSize:
						return _overridingMaxTextureSizeMessage;
					case DataId.TextureResizeAlgorithm:
						return _overridingTextureResizeAlgorithmMessage;
					case DataId.TextureFormat:
						return _overridingTextureFormatMessage;
					case DataId.CompressionType:
						return _overridingCompressionTypeMessage;
					case DataId.CompressionIsCrunched:
						return _overridingCompressionIsCrunchedMessage;
					case DataId.CompressionQuality:
						return _overridingCompressionQualityMessage;
					// ------------------------
					default:
						return null;
				}
			}
		}

		// ==================================================================================

		Dictionary<string, Entry> _textureData = new Dictionary<string, Entry>();

		public List<string> Assets;
		public List<Entry> Data;

		public Dictionary<string, Entry> GetTextureData()
		{
			return _textureData;
		}

		public void Clear()
		{
			_textureData.Clear();
		}

		// ==================================================================================

		public void OnBeforeSave()
		{
			if (Assets != null)
			{
				Assets.Clear();
			}
			else
			{
				Assets = new List<string>();
			}

			Assets.AddRange(_textureData.Keys);

			if (Data != null)
			{
				Data.Clear();
			}
			else
			{
				Data = new List<Entry>();
			}

			Data.AddRange(_textureData.Values);
		}

		public void OnAfterLoad()
		{
			_textureData.Clear();

			var platformName = GetPlatformNameFromBuildType(BuildType);
			var len = Mathf.Min(Assets.Count, Data.Count);
			for (int n = 0; n < len; ++n)
			{
				var entryToModify = Data[n];
				entryToModify.UpdateShownSettings(platformName);
				Data[n] = entryToModify; // have to assign it back, Entry is a struct

				_textureData.Add(Assets[n], Data[n]);
			}
		}

		// ==================================================================================

		public static string GetPlatformNameFromBuildType(string buildType)
		{
			if (string.IsNullOrEmpty(buildType))
			{
				return null;
			}

			if (buildType.IndexOf("Windows", StringComparison.Ordinal) != -1 &&
			    buildType.IndexOf("Store", StringComparison.Ordinal) != -1)
			{
				return "Windows Store Apps";
			}

			if (buildType.IndexOf("Windows", StringComparison.Ordinal) != -1 ||
			    buildType.IndexOf("Linux", StringComparison.Ordinal) != -1 ||
			    buildType.IndexOf("Mac", StringComparison.Ordinal) != -1)
			{
				return "Standalone";
			}

			if (buildType.IndexOf("Android", StringComparison.Ordinal) != -1)
			{
				return "Android";
			}

			if (buildType.IndexOf("iPhone", StringComparison.Ordinal) != -1 ||
			    buildType.IndexOf("iOS", StringComparison.Ordinal) != -1)
			{
				return "iPhone";
			}

			if (buildType.IndexOf("Tizen", StringComparison.Ordinal) != -1)
			{
				return "Tizen";
			}

			if (buildType.IndexOf("WebPlayer", StringComparison.Ordinal) != -1)
			{
				return "Web";
			}

			if (buildType.IndexOf("WebGL", StringComparison.Ordinal) != -1)
			{
				return "WebGL";
			}

			if (buildType.IndexOf("XBoxOne", StringComparison.OrdinalIgnoreCase) != -1)
			{
				return "XboxOne";
			}

			if (buildType.IndexOf("PS4", StringComparison.OrdinalIgnoreCase) != -1)
			{
				return "PS4";
			}

			if (buildType.IndexOf("PSVitaNative", StringComparison.OrdinalIgnoreCase) != -1)
			{
				return "PSP2";
			}

			if (buildType.IndexOf("WiiU", StringComparison.OrdinalIgnoreCase) != -1)
			{
				return "WiiU";
			}

			if (buildType.IndexOf("Nintendo", StringComparison.OrdinalIgnoreCase) != -1 &&
			    buildType.IndexOf("3DS", StringComparison.OrdinalIgnoreCase) != -1)
			{
				return "Nintendo 3DS";
			}

			if (buildType.IndexOf("Nintendo", StringComparison.OrdinalIgnoreCase) != -1 &&
			    buildType.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) != -1)
			{
				return "Nintendo Switch";
			}

			return buildType;
		}

		// ==================================================================================
	}
}