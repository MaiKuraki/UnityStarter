////////////////////////////////////////////////////////////////////////////////
//
// MobileVibration Example
// Demonstrates mobile haptic feedback: presets, sharpness, curves, HapticClip,
// real-time Core Haptics modulation, and legacy vibration APIs.
//
////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CycloneGames.DeviceFeedback.Runtime;
using UnityEngine;
using UnityEngine.UI;

public class MobileVibrationExample : MonoBehaviour
{
    [Header("Status")]
    public Text txtAndroidVersion;
    public Text txtHasVibrator;

    [Header("Custom Vibration")]
    public InputField inputTime;
    public InputField inputPattern;
    public InputField inputRepeat;

    [Header("Sharpness / Intensity")]
    public Slider sliderIntensity;
    public Slider sliderSharpness;
    public Text txtIntensityValue;
    public Text txtSharpnessValue;

    [Header("Curve Vibration")]
    public AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve sharpnessCurve = AnimationCurve.Linear(0f, 0.2f, 1f, 1.0f);

    [Header("HapticClip")]
    public HapticClip hapticClip;

    [Header("Toggle")]
    public Toggle toggleVibration;

    private static readonly Regex PatternRegex = new Regex(@"^\s*\d+\s*(,\s*\d+\s*)*$", RegexOptions.Compiled);

    void Start()
    {
        MobileVibration.Init();
        txtAndroidVersion.text = "Android SDK: " + MobileVibration.AndroidSdkVersion;
        if (txtHasVibrator != null)
            txtHasVibrator.text = "HasVibrator: " + MobileVibration.HasVibrator;

        if (inputTime != null) inputTime.contentType = InputField.ContentType.IntegerNumber;
        if (inputRepeat != null) inputRepeat.contentType = InputField.ContentType.IntegerNumber;
        if (inputPattern != null) inputPattern.onValidateInput += ValidatePatternChar;
        if (toggleVibration != null)
        {
            toggleVibration.isOn = true;
            toggleVibration.onValueChanged.AddListener(TapToggleVibration);
        }

        if (sliderIntensity != null) { sliderIntensity.minValue = 0f; sliderIntensity.maxValue = 1f; sliderIntensity.value = 0.8f; sliderIntensity.onValueChanged.AddListener(OnIntensityChanged); }
        if (sliderSharpness != null) { sliderSharpness.minValue = 0f; sliderSharpness.maxValue = 1f; sliderSharpness.value = 0.5f; sliderSharpness.onValueChanged.AddListener(OnSharpnessChanged); }

        OnIntensityChanged(sliderIntensity != null ? sliderIntensity.value : 0.8f);
        OnSharpnessChanged(sliderSharpness != null ? sliderSharpness.value : 0.5f);
    }

    private void OnIntensityChanged(float value)
    {
        if (txtIntensityValue != null) txtIntensityValue.text = "Intensity: " + value.ToString("F2");
    }

    private void OnSharpnessChanged(float value)
    {
        if (txtSharpnessValue != null) txtSharpnessValue.text = "Sharpness: " + value.ToString("F2");
    }

    private static char ValidatePatternChar(string text, int charIndex, char addedChar)
    {
        if (char.IsDigit(addedChar) || addedChar == ',' || addedChar == ' ') return addedChar;
        return '\0';
    }

    // ── Default vibration ──

    public void TapVibrate() => MobileVibration.Vibrate();

    // ── Custom duration (ms) ──

    public void TapVibrateCustom()
    {
        if (long.TryParse(inputTime.text.Trim(), out long ms) && ms > 0)
            MobileVibration.Vibrate(ms);
    }

    // ── Pattern: comma-separated [wait, vibrate, wait, vibrate, ...] ──

    public void TapVibratePattern()
    {
        string raw = inputPattern.text;
        if (string.IsNullOrWhiteSpace(raw) || !PatternRegex.IsMatch(raw)) return;

        string[] parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        var values = new List<long>(parts.Length);
        foreach (string part in parts)
        {
            if (long.TryParse(part.Trim(), out long v))
                values.Add(v);
        }
        if (values.Count == 0) return;

        int repeat = -1;
        if (inputRepeat != null && !string.IsNullOrWhiteSpace(inputRepeat.text))
            int.TryParse(inputRepeat.text.Trim(), out repeat);

        MobileVibration.Vibrate(values.ToArray(), repeat);
    }

    public void TapCancelVibrate() => MobileVibration.Cancel();

    // ── Classic haptic shortcuts ──

    public void TapPopVibrate() => MobileVibration.VibratePop();
    public void TapPeekVibrate() => MobileVibration.VibratePeek();
    public void TapNopeVibrate() => MobileVibration.VibrateNope();

    // ── Unified haptic presets ──

    public void TapPresetLight() => MobileVibration.PlayPreset(HapticPreset.Light);
    public void TapPresetMedium() => MobileVibration.PlayPreset(HapticPreset.Medium);
    public void TapPresetHeavy() => MobileVibration.PlayPreset(HapticPreset.Heavy);
    public void TapPresetSuccess() => MobileVibration.PlayPreset(HapticPreset.Success);
    public void TapPresetWarning() => MobileVibration.PlayPreset(HapticPreset.Warning);
    public void TapPresetError() => MobileVibration.PlayPreset(HapticPreset.Error);
    public void TapPresetSelection() => MobileVibration.PlayPreset(HapticPreset.Selection);

    public void TapVibratePreset(string presetName)
    {
        if (Enum.TryParse<HapticPreset>(presetName, out var preset))
            MobileVibration.PlayPreset(preset);
    }

    // ── Play with intensity + sharpness (slider-driven) ──

    public void TapPlayWithSharpness()
    {
        float intensity = sliderIntensity != null ? sliderIntensity.value : 0.8f;
        float sharpness = sliderSharpness != null ? sliderSharpness.value : 0.5f;
        MobileVibration.Play(intensity, 0.3f, sharpness);
    }

    // ── Dual-curve vibration (intensity + sharpness curves) ──

    public void TapPlayDualCurve()
    {
        MobileVibration.PlayCurve(intensityCurve, 2.0f, sharpnessCurve, sampleIntervalMs: 15);
    }

    // ── HapticClip playback ──

    public void TapPlayClip()
    {
        if (hapticClip != null)
            MobileVibration.PlayClip(hapticClip);
    }

    // ── Real-time parameter modulation (iOS Core Haptics) ──
    // Call from Update() while a continuous haptic is active to dynamically
    // change intensity/sharpness in response to game state.

    public void TapStartContinuous()
    {
        MobileVibration.Play(0.5f, 5.0f, 0.5f);
    }

    private bool _modulationActive;

    public void TapToggleModulation()
    {
        _modulationActive = !_modulationActive;
    }

    void Update()
    {
        if (!_modulationActive) return;

        float intensity = sliderIntensity != null ? sliderIntensity.value : 0.5f;
        float sharpness = sliderSharpness != null ? sliderSharpness.value : 0.5f;
        MobileVibration.UpdateContinuousParameters(intensity, sharpness);
    }

    // ── iOS-specific Haptic Feedback (no-op on other platforms) ──

    public void TapIOSImpactLight() => MobileVibration.VibrateIOS(IOSImpactStyle.Light);
    public void TapIOSImpactMedium() => MobileVibration.VibrateIOS(IOSImpactStyle.Medium);
    public void TapIOSImpactHeavy() => MobileVibration.VibrateIOS(IOSImpactStyle.Heavy);
    public void TapIOSImpactRigid() => MobileVibration.VibrateIOS(IOSImpactStyle.Rigid);
    public void TapIOSImpactSoft() => MobileVibration.VibrateIOS(IOSImpactStyle.Soft);

    public void TapIOSNotifSuccess() => MobileVibration.VibrateIOS(IOSNotificationStyle.Success);
    public void TapIOSNotifWarning() => MobileVibration.VibrateIOS(IOSNotificationStyle.Warning);
    public void TapIOSNotifError() => MobileVibration.VibrateIOS(IOSNotificationStyle.Error);

    public void TapIOSSelection() => MobileVibration.VibrateIOSSelection();

    // ── Toggle vibration on/off ──

    public void TapToggleVibration(bool active)
    {
        MobileVibration.SetActive(active);
        if (txtHasVibrator != null)
            txtHasVibrator.text = "Vibration: " + (active ? "ON" : "OFF");
    }
}
