using CycloneGames.Logger;

#if TND_UPSCALING_PRESENT
using TND.Upscaling.Framework;
#endif

namespace CycloneGames.Service.Runtime
{
    /// <summary>
    /// Manages upscaler settings and provides TndQuality for UpscalerController_URP.
    /// 
    /// Usage:
    ///   var provider = new UpscalerSettingsDefaultProvider();
    ///   upscalerService.ApplySettings(provider.GetDefault());
    ///   // Apply upscalerService.TndQuality to UpscalerController_URP in scene
    /// </summary>
    public interface IUpscalerSettingService
    {
        void ApplySettings(in UpscalerSettingsData settings);
        bool IsUpscalerActive { get; }
        UpscalerTechnology CurrentTechnology { get; }
        UpscalerQualityPreset CurrentQuality { get; }
        float CurrentScaleFactor { get; }
        UpscalerTechnology[] GetSupportedTechnologies();

#if TND_UPSCALING_PRESENT
        /// <summary>
        /// TND UpscalerQuality enum value to use with UpscalerController_URP.
        /// </summary>
        UpscalerQuality TndQuality { get; }
#endif
    }

    public sealed class UpscalerSettingService : IUpscalerSettingService
    {
        private const string DEBUG_FLAG = "[UpscalerSettings]";

        public bool IsUpscalerActive { get; private set; }
        public UpscalerTechnology CurrentTechnology { get; private set; } = UpscalerTechnology.None;
        public UpscalerQualityPreset CurrentQuality { get; private set; } = UpscalerQualityPreset.Off;
        public float CurrentScaleFactor { get; private set; } = 1f;

#if TND_UPSCALING_PRESENT
        public UpscalerQuality TndQuality { get; private set; } = UpscalerQuality.Off;
#endif

        public UpscalerSettingService()
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Initialized. FSR3={UpscalerSettingsDefaultProvider.IsFSR3Available()}, SGSR2={UpscalerSettingsDefaultProvider.IsSGSR2Available()}");
        }

        public void ApplySettings(in UpscalerSettingsData settings)
        {
            CurrentTechnology = settings.Technology;
            CurrentQuality = settings.QualityPreset;
            CurrentScaleFactor = UpscalerSettingsData.GetScaleFactorForPreset(settings.QualityPreset);

            if (settings.Technology == UpscalerTechnology.None || settings.QualityPreset == UpscalerQualityPreset.Off)
            {
                IsUpscalerActive = false;
#if TND_UPSCALING_PRESENT
                TndQuality = UpscalerQuality.Off;
#endif
                CLogger.LogInfo($"{DEBUG_FLAG} Upscaler disabled");
                return;
            }

#if TND_UPSCALING_PRESENT
            TndQuality = MapQualityPreset(settings.QualityPreset);
            IsUpscalerActive = true;
            CLogger.LogInfo($"{DEBUG_FLAG} Settings applied: {settings.Technology} @ {settings.QualityPreset} (TndQuality={TndQuality})");
#else
            IsUpscalerActive = false;
            CLogger.LogWarning($"{DEBUG_FLAG} TND Upscaling Framework not present");
#endif
        }

#if TND_UPSCALING_PRESENT
        private static UpscalerQuality MapQualityPreset(UpscalerQualityPreset preset)
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

        public UpscalerTechnology[] GetSupportedTechnologies()
        {
            return UpscalerSettingsDefaultProvider.GetAvailableTechnologies();
        }
    }
}