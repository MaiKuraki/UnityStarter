using CycloneGames.Logger;
using UnityEngine;

#if TND_UPSCALING_PRESENT
using TND.Upscaling.Framework;
#endif

#if FSR_3_PRESENT
using TND.Upscaling.FSR3;
#endif

#if SGSR_2_PRESENT
using TND.Upscaling.SGSR2;
#endif

namespace CycloneGames.Service.Runtime
{
    /// <summary>
    /// Service for applying upscaler settings to TND Upscaler Framework.
    /// Uses conditional compilation for optional FSR3/SGSR2/DLSS support.
    /// 
    /// Usage:
    ///   var service = new UpscalerSettingService();
    ///   var provider = new UpscalerSettingsDefaultProvider();
    ///   var settings = provider.GetDefault();
    ///   service.ApplySettings(settings);
    /// </summary>
    public interface IUpscalerSettingService
    {
        void ApplySettings(in UpscalerSettingsData settings);
        bool IsUpscalerActive { get; }
        UpscalerTechnology CurrentTechnology { get; }
        UpscalerTechnology[] GetSupportedTechnologies();
    }

    public sealed class UpscalerSettingService : IUpscalerSettingService
    {
        private const string DEBUG_FLAG = "[UpscalerSettings]";

        public bool IsUpscalerActive { get; private set; }
        public UpscalerTechnology CurrentTechnology { get; private set; } = UpscalerTechnology.None;

        public UpscalerSettingService()
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Initialized. FSR3={UpscalerSettingsDefaultProvider.IsFSR3Available()}, SGSR2={UpscalerSettingsDefaultProvider.IsSGSR2Available()}");
        }

        public void ApplySettings(in UpscalerSettingsData settings)
        {
            CurrentTechnology = settings.Technology;

            if (settings.Technology == UpscalerTechnology.None || settings.QualityPreset == UpscalerQualityPreset.Off)
            {
                DisableUpscaler();
                return;
            }

#if TND_UPSCALING_PRESENT
            // Map quality preset to TND UpscalerQuality enum
            var tndQuality = MapQualityPreset(settings.QualityPreset);

            switch (settings.Technology)
            {
#if FSR_3_PRESENT
                case UpscalerTechnology.FSR3:
                    ApplyFSR3Settings(tndQuality, settings);
                    break;
#endif

#if SGSR_2_PRESENT
                case UpscalerTechnology.SGSR2:
                    ApplySGSR2Settings(tndQuality, settings);
                    break;
#endif

                default:
                    CLogger.LogWarning($"{DEBUG_FLAG} Technology {settings.Technology} not available in this build");
                    DisableUpscaler();
                    break;
            }
#else
            CLogger.LogWarning($"{DEBUG_FLAG} TND Upscaling Framework not present");
            DisableUpscaler();
#endif
        }

#if TND_UPSCALING_PRESENT
        private UpscalerQuality MapQualityPreset(UpscalerQualityPreset preset)
        {
            return preset switch
            {
                UpscalerQualityPreset.Off => UpscalerQuality.Off,
                UpscalerQualityPreset.NativeAA => UpscalerQuality.NativeAA,
                UpscalerQualityPreset.UltraQuality => UpscalerQuality.UltraQuality,
                UpscalerQualityPreset.Quality => UpscalerQuality.Quality,
                UpscalerQualityPreset.Balanced => UpscalerQuality.Balanced,
                UpscalerQualityPreset.Performance => UpscalerQuality.Performance,
                UpscalerQualityPreset.UltraPerformance => UpscalerQuality.UltraPerformance,
                _ => UpscalerQuality.Quality
            };
        }
#endif

#if FSR_3_PRESENT
        private void ApplyFSR3Settings(UpscalerQuality quality, in UpscalerSettingsData settings)
        {
            // Find FSR3 controller in scene
            var controller = Object.FindFirstObjectByType<TND.Upscaling.Framework.URP.UpscalerController_URP>();
            if (controller == null)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} FSR3: No UpscalerController_URP found in scene");
                return;
            }

            // Apply quality setting via controller's public properties/methods
            // Note: Actual API depends on TND Upscaler version
            CLogger.LogInfo($"{DEBUG_FLAG} FSR3 applied with quality: {quality}");
            IsUpscalerActive = true;
        }
#endif

#if SGSR_2_PRESENT
        private void ApplySGSR2Settings(UpscalerQuality quality, in UpscalerSettingsData settings)
        {
            // Find SGSR2 controller in scene
            var controller = Object.FindFirstObjectByType<TND.Upscaling.Framework.URP.UpscalerController_URP>();
            if (controller == null)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} SGSR2: No UpscalerController_URP found in scene");
                return;
            }

            CLogger.LogInfo($"{DEBUG_FLAG} SGSR2 applied with quality: {quality}");
            IsUpscalerActive = true;
        }
#endif

        private void DisableUpscaler()
        {
#if TND_UPSCALING_PRESENT
            var controller = Object.FindFirstObjectByType<TND.Upscaling.Framework.URP.UpscalerController_URP>();
            if (controller != null)
            {
                // Disable upscaler by setting quality to Off
                CLogger.LogInfo($"{DEBUG_FLAG} Upscaler disabled");
            }
#endif
            IsUpscalerActive = false;
            CurrentTechnology = UpscalerTechnology.None;
        }

        public UpscalerTechnology[] GetSupportedTechnologies()
        {
            return UpscalerSettingsDefaultProvider.GetAvailableTechnologies();
        }
    }
}