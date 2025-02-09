#define BRT_USE_INTERNAL_TEXTURE_IMPORTER_METHODS

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BuildReportTool
{
	public static class TextureDataGenerator
	{
		public static void CreateForUsedAssetsOnly(TextureData data, BuildReportTool.BuildInfo buildInfo, bool debugLog = false)
		{
			if (buildInfo == null)
			{
				if (debugLog) Debug.LogError("Can't create TextureData for Used Assets, BuildInfo is null");
				return;
			}
			if (debugLog) Debug.Log("Will create TextureData for Used Assets");

			BuildReportTool.BuildPlatform buildPlatform = BuildReportTool.ReportGenerator.GetBuildPlatformFromString(buildInfo.BuildType, buildInfo.BuildTargetUsed);

			var textureDataEntries = data.GetTextureData();
			textureDataEntries.Clear();

			AppendTextureData(data, buildPlatform, buildInfo.UsedAssets.All, false, debugLog);
		}

		public static void CreateForAllAssets(TextureData data, BuildReportTool.BuildInfo buildInfo, bool debugLog = false)
		{
			if (buildInfo == null)
			{
				if (debugLog) Debug.LogError("Can't create TextureData for Used & Unused Assets, BuildInfo is null");
				return;
			}
			if (debugLog) Debug.Log("Will create TextureData for Used & Unused Assets");

			BuildReportTool.BuildPlatform buildPlatform = BuildReportTool.ReportGenerator.GetBuildPlatformFromString(buildInfo.BuildType, buildInfo.BuildTargetUsed);

			var textureDataEntries = data.GetTextureData();
			textureDataEntries.Clear();

			AppendTextureData(data, buildPlatform, buildInfo.UsedAssets.All, false, debugLog);
			AppendTextureData(data, buildPlatform, buildInfo.UnusedAssets.All, false, debugLog);
		}

		static void AppendTextureData(TextureData data, BuildReportTool.BuildPlatform buildPlatform, IList<SizePart> assets, bool overwriteExistingEntries, bool debugLog = false)
		{
			if (debugLog) Debug.LogFormat("Creating Texture Data for {0} assets", assets.Count.ToString());

			var platformString = GetPlatformString(buildPlatform);
			var textureDataEntries = data.GetTextureData();

			for (int n = 0; n < assets.Count; ++n)
			{
				if (!Util.IsTextureFile(assets[n].Name))
				{
					// this asset is not an image, skip it
					continue;
				}

				if (textureDataEntries.ContainsKey(assets[n].Name))
				{
					if (!overwriteExistingEntries)
					{
						continue;
					}
					else
					{
						var newEntry = CreateEntry(assets[n].Name, platformString, debugLog);
						textureDataEntries[assets[n].Name] = newEntry;
					}
				}
				else
				{
					var newEntry = CreateEntry(assets[n].Name, platformString, debugLog);
					textureDataEntries.Add(assets[n].Name, newEntry);
				}
			}
		}

		const int ANISO_LEVEL_IF_VALUE_IS_NEGATIVE_ONE = 1;
		const int COMPRESSION_QUALITY_IF_VALUE_IS_NEGATIVE_ONE = 50;
		const string WRAP_MODE_IF_VALUE_IS_NEGATIVE_ONE = "Repeat";

		static TextureData.Entry CreateEntry(string assetPath, string platform, bool debugLog = false)
		{
			var assetImporter = AssetImporter.GetAtPath(assetPath);
			if (assetImporter == null)
			{
				if (debugLog) Debug.LogErrorFormat("AssetImporter.GetAtPath returned null for {0}", assetPath);
				return new TextureData.Entry();
			}

			var textureImporter = assetImporter as TextureImporter;
			if (textureImporter == null)
			{
				if (debugLog) Debug.LogErrorFormat("AssetImporter is not a TextureImporter for {0}", assetPath);
				return new TextureData.Entry();
			}

			// -----------------------------------------------------------------------

			if (debugLog) Debug.LogFormat("Inspecting Texture: {0}", assetPath);

			var result = new TextureData.Entry();

			// -----------------------------------------------------------------------

			// textureImporter.textureType: enum (whether it's GUI, lightmap, normal map, sprite, etc.)
			result.TextureType = TextureTypeToReadableString(textureImporter.textureType);

#if UNITY_5_5_OR_NEWER
			result.IsSRGB = textureImporter.sRGBTexture;
#else
			result.IsSRGB = !textureImporter.linearTexture; // obsolete in Unity 5.5
#endif
			if (result.IsSRGB)
			{
				switch (textureImporter.textureType)
				{
#if UNITY_5_5_OR_NEWER
					case TextureImporterType.NormalMap:
#else
					case TextureImporterType.Bump:
#endif
					case TextureImporterType.Lightmap:
#if UNITY_5_5_OR_NEWER
					case TextureImporterType.SingleChannel:
#endif
						if (debugLog) Debug.LogWarningFormat("Texture: {0} was marked as sRGB but it is a {1}", assetPath, result.TextureType);
						result.IsSRGB = false;
						break;
				}
			}

#if UNITY_5_5_OR_NEWER
			result.AlphaSource = textureImporter.alphaSource.ToString();
#else
			result.AlphaSource = null;
#endif
			result.AlphaIsTransparency = textureImporter.alphaIsTransparency;
#if UNITY_2020_1_OR_NEWER
			result.IgnorePngGamma = textureImporter.ignorePngGamma;
#else
			result.IgnorePngGamma = false; // doesn't exist yet in Unity 2019 and below
#endif

			// -----------------------------------------------------------------------

			result.IsReadable = textureImporter.isReadable;
			result.MipMapGenerated = textureImporter.mipmapEnabled;
			result.MipMapFilter = textureImporter.mipmapFilter.ToString();
#if UNITY_2018_2_OR_NEWER
			result.StreamingMipMaps = textureImporter.streamingMipmaps;
#else
			result.StreamingMipMaps = false; // doesn't exist yet in Unity 2018.1 and below
#endif
			result.BorderMipMaps = textureImporter.borderMipmap;
#if UNITY_2017_1_OR_NEWER
			result.PreserveCoverageMipMaps = textureImporter.mipMapsPreserveCoverage;
#else
			result.PreserveCoverageMipMaps = false;
#endif
			result.FadeOutMipMaps = textureImporter.fadeout;

			// -----------------------------------------------------------------------

			result.SpriteImportMode = textureImporter.spriteImportMode.ToString();
#if !UNITY_2022_2_OR_NEWER
			result.SpritePackingTag = textureImporter.spritePackingTag;
#else
			result.SpritePackingTag = null; // Sprite Packing Tag removed, replaced with Sprite Atlas assets
#endif
			result.SpritePixelsPerUnit = textureImporter.spritePixelsPerUnit;
			result.QualifiesForSpritePacking = textureImporter.qualifiesForSpritePacking;

			// -----------------------------------------------------------------------

			result.WrapMode = WrapModeToReadableString(textureImporter.wrapMode);
#if UNITY_2017_1_OR_NEWER
			result.WrapModeU = WrapModeToReadableString(textureImporter.wrapModeU);
			result.WrapModeV = WrapModeToReadableString(textureImporter.wrapModeV);
			result.WrapModeW = WrapModeToReadableString(textureImporter.wrapModeW);
#else
			result.WrapModeU = null;
			result.WrapModeV = null;
			result.WrapModeW = null;
#endif

			result.FilterMode = textureImporter.filterMode.ToString();

			result.AnisoLevel = textureImporter.anisoLevel;

			if (result.AnisoLevel == -1)
			{
				result.AnisoLevel = ANISO_LEVEL_IF_VALUE_IS_NEGATIVE_ONE;
			}

			// -----------------------------------------------------------------------

			result.MaxTextureSize = textureImporter.maxTextureSize;
#if UNITY_5_5_OR_NEWER
			var defaultSettings = textureImporter.GetDefaultPlatformTextureSettings();

#if UNITY_2017_2_OR_NEWER
			result.TextureResizeAlgorithm = defaultSettings.resizeAlgorithm.ToString();
#else
			result.TextureResizeAlgorithm = string.Empty;
#endif
			if (defaultSettings.format == TextureImporterFormat.Automatic)
			{
				result.TextureFormat = textureImporter.GetAutomaticFormat(platform).ToString();
			}
			else
			{
				result.TextureFormat = defaultSettings.format.ToString();
			}
			result.CompressionType = CompressionTypeToReadableString(defaultSettings.textureCompression);
			result.CompressionIsCrunched = defaultSettings.crunchedCompression;
#else
			// Unity 5.4 and below
			result.TextureResizeAlgorithm = null;
			result.TextureFormat = TextureFormatToReadableString(textureImporter.textureFormat);
			result.CompressionType = null; // no compression type in Unity 5.4 and below
			result.CompressionIsCrunched = false; // no crunch compression in Unity 5.4 and below
#endif
			result.CompressionQuality = textureImporter.compressionQuality;
			if (result.CompressionQuality == -1)
			{
				result.CompressionQuality = COMPRESSION_QUALITY_IF_VALUE_IS_NEGATIVE_ONE;
			}

			// -----------------------------------------------------------------------

#if UNITY_5_5_OR_NEWER
			var overrideSettings = !string.IsNullOrEmpty(platform) ? textureImporter.GetPlatformTextureSettings(platform) : null;
			if (overrideSettings != null && overrideSettings.overridden)
			{
				result.PlatformSettingsOverriden = true;
				result.OverridingMaxTextureSize = overrideSettings.maxTextureSize;
#if UNITY_2017_2_OR_NEWER
				result.OverridingTextureResizeAlgorithm = overrideSettings.resizeAlgorithm.ToString();
#else
				result.OverridingTextureResizeAlgorithm = string.Empty;
#endif
				if (overrideSettings.format == TextureImporterFormat.Automatic)
				{
					result.OverridingTextureFormat = textureImporter.GetAutomaticFormat(platform).ToString();
				}
				else
				{
					result.OverridingTextureFormat = overrideSettings.format.ToString();
				}
				result.OverridingCompressionType = CompressionTypeToReadableString(overrideSettings.textureCompression);
				result.OverridingCompressionIsCrunched = overrideSettings.crunchedCompression;
				result.OverridingCompressionQuality = overrideSettings.compressionQuality;
				if (result.OverridingCompressionQuality == -1)
				{
					result.OverridingCompressionQuality = COMPRESSION_QUALITY_IF_VALUE_IS_NEGATIVE_ONE;
				}

				if (debugLog && result.TextureFormat != result.OverridingTextureFormat)
					Debug.LogFormat("TextureDataGenerator: {0} for {1} format overriden from {2} to {3}",
						platform, assetPath, result.TextureFormat, result.OverridingTextureFormat);

				if (debugLog && result.MaxTextureSize != result.OverridingMaxTextureSize)
					Debug.LogFormat("TextureDataGenerator: {0} for {1} max size overriden from {2} to {3}",
						platform, assetPath, result.MaxTextureSize.ToString(), result.OverridingMaxTextureSize.ToString());
			}
			else
#endif
			{
				result.PlatformSettingsOverriden = false;
				result.OverridingMaxTextureSize = 0;
				result.OverridingTextureResizeAlgorithm = null;
				result.OverridingTextureFormat = null;
				result.OverridingCompressionType = null;
				result.OverridingCompressionIsCrunched = false;
				result.OverridingCompressionQuality = 0;
			}

			// -----------------------------------------------------------------------

			// Note: there are two import settings can make the
			// imported width/height different from the real width/height:
			// MaxTextureSize (which is a max limit on the value),
			// and NPotScale (which will resize to a power-of-two value if specified)

			var loadedTexture = AssetDatabase.LoadAssetAtPath(assetPath, typeof(Texture)) as Texture;

			if (loadedTexture != null)
			{
				result.ImportedWidth = loadedTexture.width;
				result.ImportedHeight = loadedTexture.height;

				if (debugLog)
					Debug.LogFormat("Got imported dimensions (using AssetDatabase.LoadAssetAtPath Texture) for {0} {1}x{2}",
						assetPath, result.ImportedWidth.ToString(), result.ImportedHeight.ToString());
			}
			else
			{
				if (debugLog)
					Debug.LogErrorFormat("Could not get imported width and height, AssetDatabase.LoadAssetAtPath returned null for {0}", assetPath);

				// could not load texture, so we can't get imported width and height
				result.ImportedWidth = 0;
				result.ImportedHeight = 0;
			}

			result.UpdateShownSettings(platform);

			if (textureImporter.npotScale == TextureImporterNPOTScale.None)
			{
				if (Mathf.IsPowerOfTwo(result.ImportedWidth) && Mathf.IsPowerOfTwo(result.ImportedHeight))
				{
					result.NPotScale = BuildReportTool.TextureData.NPOT_SCALE_NONE_IS_POT;
				}
				else
				{
					result.NPotScale = BuildReportTool.TextureData.NPOT_SCALE_NONE_NOT_POT;
				}
			}
			else
			{
				result.NPotScale = textureImporter.npotScale.ToString();
			}

			var gotDimensions = GetImageRealWidthAndHeight(assetPath, textureImporter, debugLog);
			result.RealWidth = gotDimensions.Width;
			result.RealHeight = gotDimensions.Height;

			return result;
		}

		// ========================================================================================

#if !UNITY_5_5_OR_NEWER
		static string TextureFormatToReadableString(TextureImporterFormat textureFormat)
		{
			return textureFormat.ToString();
		}
#endif

		static string TextureTypeToReadableString(TextureImporterType textureType)
		{
			switch (textureType)
			{
#if UNITY_5_5_OR_NEWER
				case TextureImporterType.Default:
					return "Default";
				case TextureImporterType.SingleChannel:
					return "Single Channel";
				case TextureImporterType.NormalMap:
					return "Normal Map";
#else
				case TextureImporterType.Bump:
					return "Normal Map";
#endif
				case TextureImporterType.GUI:
					return "GUI";
				case TextureImporterType.Sprite:
					return "Sprite";
				case TextureImporterType.Cursor:
					return "Cursor";
				case TextureImporterType.Cookie:
					return "Cookie";
				case TextureImporterType.Lightmap:
					return "Lightmap";
#if UNITY_2020_2_OR_NEWER
				case TextureImporterType.DirectionalLightmap:
					return "Directional Lightmap";
#endif
				default:
					return textureType.ToString();
			}
		}

		static string WrapModeToReadableString(TextureWrapMode wrapMode)
		{
			switch (wrapMode)
			{
				case TextureWrapMode.Repeat:
					return "Repeat";
				case TextureWrapMode.Clamp:
					return "Clamp";
#if UNITY_2017_1_OR_NEWER
				case TextureWrapMode.Mirror:
					return "Mirror";
				case TextureWrapMode.MirrorOnce:
					return "MirrorOnce";
#endif
				default:
					if ((int)wrapMode == -1)
					{
						return WRAP_MODE_IF_VALUE_IS_NEGATIVE_ONE;
					}
					return string.Format("Unrecognized ({0})", wrapMode.ToString());
			}
		}

#if UNITY_5_5_OR_NEWER
		static string CompressionTypeToReadableString(TextureImporterCompression compression)
		{
			switch (compression)
			{
				case TextureImporterCompression.Uncompressed:
					return "Uncompressed";
				case TextureImporterCompression.Compressed:
					return "Standard Compression";
				case TextureImporterCompression.CompressedHQ:
					return "High Quality, Low Compression";
				case TextureImporterCompression.CompressedLQ:
					return "Low Quality, High Compression";
				default:
					return compression.ToString();
			}
		}
#endif

		public static string GetPlatformString(BuildPlatform buildPlatform)
		{
			// options for the platform string are:
			// in Unity    5.5: "Standalone", "Web", "iPhone", "Android", "WebGL", "Windows Store Apps", "Tizen", "PSP2", "PS4", "XboxOne", "Samsung TV", "Nintendo 3DS", "WiiU" and "tvOS"
			// in Unity 2017.4: "Standalone", "Web", "iPhone", "Android", "WebGL", "Windows Store Apps", "PSP2", "PS4", "XboxOne", "Nintendo 3DS", "WiiU" and "tvOS". Tizen & Samsung TV removed
			// in Unity 2018.1: "Standalone", "Web", "iPhone", "Android", "WebGL", "Windows Store Apps", "PSP2", "PS4", "XboxOne", "Nintendo 3DS" and "tvOS". WiiU removed
			// in Unity 2018.2: "Standalone", "Web", "iPhone", "Android", "WebGL", "Windows Store Apps", "PSP2", "PS4", "XboxOne", "Nintendo 3DS" and "tvOS". no change
			// in Unity 2018.3: "Standalone", "Web", "iPhone", "Android", "WebGL", "Windows Store Apps", "PS4", "XboxOne", "Nintendo 3DS" and "tvOS". PSP2 (i.e. PS Vita) removed
			// in Unity 2018.4: "Standalone", "Web", "iPhone", "Android", "WebGL", "Windows Store Apps", "PS4", "XboxOne", "Nintendo 3DS" and "tvOS". no change
			// in Unity 2019.3: "Standalone", "Web", "iPhone", "Android", "WebGL", "Windows Store Apps", "PS4", "XboxOne", "Nintendo 3DS" and "tvOS". no change
			// in Unity 2019.4: "Standalone", "Web", "iPhone", "Android", "WebGL", "Windows Store Apps", "PS4", "XboxOne", "Nintendo Switch" and "tvOS". 3DS removed, Switch added
			// in Unity 2020.3: "Standalone", "Web", "iPhone", "Android", "WebGL", "Windows Store Apps", "PS4", "XboxOne", "Nintendo Switch" and "tvOS". no change
			// in Unity 2021.1: "Standalone", "Web", "iPhone", "Android", "WebGL", "Windows Store Apps", "PS4", "XboxOne", "Nintendo Switch" and "tvOS". no change

			switch (buildPlatform)
			{
				case BuildPlatform.Android:
					return "Android";
				case BuildPlatform.iOS:
					return "iPhone";
				case BuildPlatform.Tizen:
					return "Tizen";
				case BuildPlatform.Web:
					return "Web";
				case BuildPlatform.WebGL:
					return "WebGL";
				case BuildPlatform.MacOSX32:
				case BuildPlatform.MacOSX64:
				case BuildPlatform.MacOSXUniversal:
				case BuildPlatform.Linux32:
				case BuildPlatform.Linux64:
				case BuildPlatform.LinuxUniversal:
				case BuildPlatform.Windows32:
				case BuildPlatform.Windows64:
					return "Standalone";
				case BuildPlatform.WindowsStoreApp:
					return "Windows Store Apps";
				case BuildPlatform.XboxOne:
					return "XboxOne";
				case BuildPlatform.XboxSeries:
					return "GameCoreXboxSeries";
				case BuildPlatform.PS4:
					return "PS4";
				case BuildPlatform.PS5:
					return "PS5";
				case BuildPlatform.PSVitaNative:
					return "PSP2";
				case BuildPlatform.WiiU:
					return "WiiU";
				case BuildPlatform.Nintendo3DS:
					return "Nintendo 3DS";
				case BuildPlatform.Switch:
					return "Nintendo Switch";
				default:
					return null;
			}
		}

		// ========================================================================================

#if BRT_USE_INTERNAL_TEXTURE_IMPORTER_METHODS
#if UNITY_2018_1_OR_NEWER
		/// <summary>
		/// <see cref="TextureImporter.GetSourceTextureInformation()"/> for getting image's real width and height.
		/// This method is private, at least as of Unity 2020.1.17f1.
		/// </summary>
		static readonly System.Reflection.MethodInfo TextureImporterGetSourceTextureInformation =
			typeof(TextureImporter).GetMethod("GetSourceTextureInformation",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
#endif

		/// <summary>
		/// <see cref="TextureImporter.GetWidthAndHeight(ref int, ref int)"/> for getting image's real width and height.
		/// This method is internal, at least as of Unity 2020.1.17f1.
		/// </summary>
		static readonly System.Reflection.MethodInfo TextureImporterGetWidthAndHeight =
			typeof(TextureImporter).GetMethod("GetWidthAndHeight",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

		static object[] _getWidthAndHeightParameters;
#endif

		static ImageUtility.Dimensions GetImageRealWidthAndHeight(string assetPath, TextureImporter textureImporter, bool debugLog = false)
		{
			// Using UnityEditor.TextureImporter.GetSourceTextureInformation is preferred, since it will work with all Unity-supported image types.
			// But since it's a private method, Unity makes no guarantee that it will still be available in future versions.
			// It's only available starting at Unity 2018.1 (doesn't exist in Unity 2017 and below).
			// Also, for Unity versions 2018.1 up to 2020.1, the return value itself (the SourceTextureInformation struct),
			// is in the UnityEditor.Experimental namespace. But starting 2020.2, it has moved out of the Experimental namespace.
			//
			// This is why we have multiple fallbacks in case GetSourceTextureInformation doesn't work.
			//
			// UnityEditor.TextureImporter.GetWidthAndHeight is a little better, since we don't need to
			// deal with the UnityEditor.Experimental namespace that way, it also works with all
			// Unity-supported image types (just like GetSourceTextureInformation), and it exists in Unity 2017.
			// The only disadvantage is that it requires allocation since it uses ref parameters.
			//
			// If for any reason, GetWidthAndHeight doesn't work, ImageUtility.Dimension.Get is our last resort.
			// It will attempt to open the file, and find the data inside that relates to width and height
			// (it doesn't require loading the entire image into memory).
			//
			// The disadvantage is that among the image types that Unity uses, and the image types
			// that ImageUtility.Dimension.Get knows, this last resort currently only works
			// for jpg (currently jfif only), png, gif, and bmp files.
			//
#if BRT_USE_INTERNAL_TEXTURE_IMPORTER_METHODS
#if UNITY_2018_1_OR_NEWER
			if (TextureImporterGetSourceTextureInformation != null)
			{
#if UNITY_2020_2_OR_NEWER // SourceTextureInformation was moved out of Experimental namespace in Unity 2020.2
				var sourceTextureInfo = (UnityEditor.AssetImporters.SourceTextureInformation)
#else
				var sourceTextureInfo = (UnityEditor.Experimental.AssetImporters.SourceTextureInformation)
#endif
					TextureImporterGetSourceTextureInformation.Invoke(textureImporter, null);

				if (debugLog) Debug.LogFormat("Got dimensions (using GetSourceTextureInformation) for {0} {1}x{2}",
					assetPath, sourceTextureInfo.width.ToString(), sourceTextureInfo.height.ToString());

				ImageUtility.Dimensions returnValue;
				returnValue.Width = sourceTextureInfo.width;
				returnValue.Height = sourceTextureInfo.height;
				return returnValue;
			}
#endif

			if (TextureImporterGetWidthAndHeight != null)
			{
				if (_getWidthAndHeightParameters == null)
				{
					_getWidthAndHeightParameters = new object[] { new int(), new int() };
				}
				TextureImporterGetWidthAndHeight.Invoke(textureImporter, _getWidthAndHeightParameters);

				ImageUtility.Dimensions returnValue;
				returnValue.Width = (int)_getWidthAndHeightParameters[0];
				returnValue.Height = (int)_getWidthAndHeightParameters[1];

				if (debugLog) Debug.LogFormat("Got dimensions (using GetWidthAndHeight) for {0} {1}x{2}",
					assetPath, returnValue.Width.ToString(), returnValue.Height.ToString());

				return returnValue;
			}
#endif
			if (assetPath.EndsWith(".jpg", System.StringComparison.OrdinalIgnoreCase) ||
			    assetPath.EndsWith(".jpeg", System.StringComparison.OrdinalIgnoreCase) ||
			    assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase) ||
			    assetPath.EndsWith(".bmp", System.StringComparison.OrdinalIgnoreCase) ||
			    assetPath.EndsWith(".gif", System.StringComparison.OrdinalIgnoreCase))
			{
				// remove 6 for the "Assets" at the start, before prefixing it with the project path
				var assetFullPath = string.Format("{0}{1}", Application.dataPath, assetPath.Substring(6));
				//if (debugLog) Debug.LogFormat("Full path of: {0}\n{1}", assetPath, assetFullPath);

				var returnValue = ImageUtility.Dimension.Get(assetFullPath);

				if (debugLog) Debug.LogFormat("Got dimensions (using ImageDimensions.Get.AsTuple) for {0} {1}x{2}",
					assetPath, returnValue.Width.ToString(), returnValue.Height.ToString());

				return returnValue;
			}

			// none of the options worked. no choice but to return error values
			return ImageUtility.Dimensions.ErrorValue;
		}
	}
}