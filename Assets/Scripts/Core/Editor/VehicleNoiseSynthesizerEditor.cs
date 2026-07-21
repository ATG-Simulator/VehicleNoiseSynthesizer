#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AroundTheGroundSimulator
{
    /// <summary>
    /// Custom inspector for <see cref="VehicleNoiseSynthesizer"/> v1.9.
    /// </summary>
    [CustomEditor(typeof(VehicleNoiseSynthesizer))]
    public class VehicleNoiseSynthesizerEditor : Editor
    {
        private VehicleNoiseSynthesizer synth;

        private SerializedProperty debugProp, debugRpmProp, debugLoadProp;
        private SerializedProperty enableDiagnosticLoggerProp, enableBurbleDiagnosticsProp;
        private SerializedProperty audioSourceTemplateProp, mixerProp, mixerTypeProp;
        private SerializedProperty masterVolumeProp, autoBlipProp, rpmDeviationProp;
        private SerializedProperty pitchCurveProp, volumeCurveProp, loadEffectivenessOnPitchProp;
        private SerializedProperty idleVolumeProp;
        private SerializedProperty keepBankClipsPlayingProp, clipVolumeResponseTimeProp, clipPitchResponseTimeProp;
        private SerializedProperty rpmResponseTimeProp, loadResponseTimeProp;
        private SerializedProperty pairHysteresisRpmProp, pairHoldCyclesProp, maxPitchRatioBeyondPairProp;
        private SerializedProperty loadCrossoverPointProp, loadBlendWidthProp;
        private SerializedProperty loadVolumeAccChangerFactorProp, loadVolumeDccChangerFactorProp;
        private SerializedProperty loadVolumeChangerMinValueProp, maxVolumeAccProp, maxVolumeDccProp;
        private SerializedProperty cylinderCountProp, combustionCycleModeProp;
        private SerializedProperty acceleratingSoundsProp, deceleratingSoundsProp;
        private SerializedProperty acPitchTrimProp, dcPitchTrimProp, maximumTheoricalRPMProp;
        private SerializedProperty lowPassStrengthProp, highPassStrengthProp, resonanceStrengthProp;
        private SerializedProperty distortionStrengthProp, chorusStrengthProp, reverbStrengthProp;
        private SerializedProperty useSharedMixerReverbProp, reverbMixerParamNameProp;
        private SerializedProperty distortionCurveProp, distortionIntensityProp, mufflingIntensityProp;
        private SerializedProperty lowPassCurveProp, lowPassIntensityProp;
        private SerializedProperty enableExhaustBurbleProp, burbleSoundsProp, burbleVolumeProp;
        private SerializedProperty burbleMinRPMProp, burbleLoadLowThresholdProp, burbleLoadHighThresholdProp, burbleRPMDropThresholdProp;
        private SerializedProperty burbleProbabilityProp, minBurbleDelayProp, maxBurbleDelayProp;
        private SerializedProperty burbleRandomPitchVariationProp, burbleFadeRateProp;
        private SerializedProperty enableDctShiftBurbleProp, dctShiftBurbleSoundProp, dctShiftBurbleVolumeProp;
        private SerializedProperty dctShiftBurbleRpmVolumeInfluenceProp, dctShiftBurbleMinRPMProp, dctShiftBurbleMaxDurationProp;
        private SerializedProperty dctShiftBurbleBasePitchProp, dctShiftBurblePitchVariationProp;
        // Throttle Body
        private SerializedProperty enableThrottleBodyProp;
        private SerializedProperty intakeRoarSoundsProp, throttleFlutterSoundsProp;
        private SerializedProperty intakeRoarVolumeProp, throttleFlutterVolumeProp;
        private SerializedProperty throttleBodyPitchVariationProp;
        private SerializedProperty throttleBodyCooldownProp;

        // Redline
        private SerializedProperty enableRedlineEffectProp, redlineSoundsProp, redlineVolumeProp;
        private SerializedProperty redlineMinRPMProp, redlineMaxRPMProp;
        private SerializedProperty redlineMinDelayProp, redlineMaxDelayProp;
        private SerializedProperty redlineBasePitchProp, redlinePitchVariationProp;
        private SerializedProperty filterLPFSlewHzProp, filterHPFSlewHzProp, filterReverbSlewDbSProp, filterParamSlewRateProp;
        private SerializedProperty enablePairSelectorDiagnosticsProp;

        private ReorderableList acceleratingList;
        private ReorderableList deceleratingList;

        private bool showDebug = true, showCore = true, showCurves = true, showBlend = false;
        private bool showCombustion = false, showAccBank = true, showDecBank = true;
        private bool showTuning = false, showFx, showBurble, showDctBurble, showThrottleBody, showRedline;
        private bool showFilterSlew;

        private bool advancedMode;
        private Dictionary<int, bool> _accExpanded;
        private Dictionary<int, bool> _decExpanded;

        private const string AdvancedPrefKey = "VNS_InspectorAdvancedMode";

        private const float TimelineHeight = 200f;
        private const float RowHeight = 24f;

        private static readonly Color BgColor = new Color(0.11f, 0.12f, 0.14f, 1f);
        private static readonly Color PanelColor = new Color(0.16f, 0.17f, 0.20f, 1f);
        private static readonly Color PanelAltColor = new Color(0.12f, 0.13f, 0.16f, 1f);
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color TextMuted = new Color(1f, 1f, 1f, 0.65f);
        private static readonly Color AccColor = new Color(0.23f, 0.76f, 1f, 1f);
        private static readonly Color DecColor = new Color(1f, 0.57f, 0.22f, 1f);
        private static readonly Color SelectedColor = new Color(0.26f, 0.90f, 0.65f, 1f);

        private GUIStyle headerStyle, subHeaderStyle, mutedLabelStyle, centeredMiniStyle;

        private void OnEnable()
        {
            synth = (VehicleNoiseSynthesizer)target;
            advancedMode = EditorPrefs.GetBool(AdvancedPrefKey, false);
            CacheProperties();
            EnsureStyles();
            _accExpanded = new Dictionary<int, bool>();
            _decExpanded = new Dictionary<int, bool>();
            RefreshClipLists();
            if (!advancedMode)
                ApplySimpleModeFoldouts();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            CacheProperties();
            EnsureStyles();

            DrawTopBar();
            EditorGUILayout.Space(6f);

            // Original section order preserved. Simple mode only changes foldout
            // open/closed state (ApplySimpleModeFoldouts) and field visibility.

            DrawFoldoutSection(ref showDebug, "Debug", () =>
            {
                EditorGUILayout.PropertyField(debugProp, new GUIContent("Debug Mode", "Enable to preview the engine sound at a manually-set RPM and load - without needing to press Play. Great for auditioning clips."));
                if (debugProp.boolValue)
                {
                    EditorGUILayout.PropertyField(debugRpmProp, new GUIContent("Debug RPM", "The RPM value used for previewing audio in the editor. Drag the slider to hear how the engine sounds at different rev ranges."));
                    EditorGUILayout.PropertyField(debugLoadProp, new GUIContent("Debug Load", "The throttle/load value (0 = off-throttle, 1 = full throttle) used for preview. Set to 0 to hear deceleration sounds, 1 for acceleration sounds."));
                }
                if (advancedMode)
                {
                    EditorGUILayout.PropertyField(enableDiagnosticLoggerProp, new GUIContent("Diagnostic Logger", "Logs detailed per-frame blend and RPM data to the Unity Console. Only turn on when troubleshooting - it produces a lot of output."));
                    EditorGUILayout.PropertyField(enableBurbleDiagnosticsProp, new GUIContent("Burble Diagnostics", "Logs burble trigger events to the Console so you can see exactly when and why a burble fires. Useful when tuning thresholds."));
                    if (enablePairSelectorDiagnosticsProp != null)
                        EditorGUILayout.PropertyField(enablePairSelectorDiagnosticsProp, new GUIContent("Pair Selector Diagnostics", "Logs pair hold/hysteresis switch stats once per second."));
                }
            });

            DrawFoldoutSection(ref showCore, "Core", () =>
            {
                EditorGUILayout.PropertyField(audioSourceTemplateProp, new GUIContent("Audio Source Template", "Optional: drag an AudioSource prefab here to use its settings (spatial blend, reverb zone mix, etc.) as the template for all engine audio sources. Leave empty to use Unity defaults."));
                EditorGUILayout.PropertyField(mixerProp, new GUIContent("Audio Mixer", "Drag your project's AudioMixer here. The engine sounds will be routed through it so you can apply bus effects and volume control from the mixer."));
                EditorGUILayout.PropertyField(mixerTypeProp, new GUIContent("Mixer Channel", "Which mixer group the engine sounds are sent to (Intake, Engine, Exhaust, etc.). Match this to the channel in your AudioMixer asset."));
                EditorGUILayout.Slider(masterVolumeProp, 0.007f, 1f, new GUIContent("Master Volume", "Overall loudness of all engine sounds. 1.0 = full volume. Lower this if the engine is too loud compared to other game audio."));
                EditorGUILayout.HelpBox("Each clip's pitch is calculated automatically: engine RPM ÷ the clip's recorded RPM. You do not need to set pitch manually.", MessageType.Info);
                if (advancedMode)
                {
                    EditorGUILayout.PropertyField(autoBlipProp, new GUIContent("Auto Blip on Downshift", "When enabled, the system applies a brief throttle blip during downshifts to match revs automatically - just like a real rev-matching gearbox."));
                    EditorGUILayout.PropertyField(rpmDeviationProp, new GUIContent("RPM Deviation", "Fallback RPM spacing used when neighbouring clip RPMs are missing, and for idle-RPM inference / edge blend diagnostics."));
                }
            });

            // Global Curves: hidden in Simple mode, full section in Advanced.
            if (advancedMode)
            {
                DrawFoldoutSection(ref showCurves, "Global Curves", () =>
                {
                    EditorGUILayout.HelpBox("These curves shape how volume and pitch respond as RPM changes. The defaults work well out of the box - only edit them if you want extra character.", MessageType.Info);
                    EditorGUILayout.PropertyField(volumeCurveProp, new GUIContent("Volume Curve", "Controls how loud the engine is at each RPM. The default rises gently with RPM. Lower the left end to reduce idle volume."));
                    EditorGUILayout.PropertyField(idleVolumeProp, new GUIContent("Idle Volume", "Volume level at zero RPM / idle. Keep this low (0.1-0.3) so the engine doesn't sound equally loud when stationary."));
                    EditorGUILayout.PropertyField(pitchCurveProp, new GUIContent("Pitch Curve", "Shapes how the overall engine pitch scales with RPM. A flat line at 1.0 is neutral - raise the right end to make the engine sound higher-pitched at redline."));
                    EditorGUILayout.PropertyField(loadEffectivenessOnPitchProp, new GUIContent("Load → Pitch", "How much engine load (throttle pressure) affects pitch. 0 = load has no effect; 1 = full effect. Most vehicles sound natural at 0.1-0.3."));
                });
            }

            DrawFoldoutSection(ref showBlend, "Blend Behaviour", () =>
            {
                EditorGUILayout.HelpBox("Controls how smoothly the engine transitions between clips as RPM and load change. Start with the defaults - only adjust if transitions feel too snappy or too sluggish.", MessageType.Info);
                EditorGUILayout.PropertyField(keepBankClipsPlayingProp, new GUIContent("Keep Bank Clips Playing", "When enabled, all clips in a bank keep playing at low volume in the background. Smoother transitions but uses more CPU. Recommended ON for quality setups."));
                EditorGUILayout.PropertyField(clipVolumeResponseTimeProp, new GUIContent("Volume Fade Time", "How many seconds it takes for a clip's volume to reach its target. Shorter = snappier blends. Try 0.08-0.2."));
                EditorGUILayout.PropertyField(clipPitchResponseTimeProp, new GUIContent("Pitch Slide Time", "How many seconds pitch takes to reach its target. Shorter values feel more responsive. Try 0.05-0.15."));
                EditorGUILayout.PropertyField(rpmResponseTimeProp, new GUIContent("RPM Smoothing", "Smooths the RPM signal before it drives audio. Higher values reduce jitter from physics but add lag. Try 0.02-0.08."));
                EditorGUILayout.PropertyField(loadResponseTimeProp, new GUIContent("Load Smoothing", "Smooths the throttle/load signal. Prevents crackling on rapid throttle changes. Try 0.05-0.15."));
                EditorGUILayout.PropertyField(loadCrossoverPointProp, new GUIContent("Acc/Dec Crossover", "Normalised load (0-1) at which the engine switches from the deceleration bank to the acceleration bank. 0.5 = mid-throttle. Adjust to taste."));
                EditorGUILayout.PropertyField(loadBlendWidthProp, new GUIContent("Crossover Blend Width", "Width of the overlap zone around the crossover point where both banks are audible simultaneously. Wider = smoother transition."));
                if (advancedMode)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("Pair selector", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(pairHysteresisRpmProp, new GUIContent("Pair Hysteresis RPM", "RPM dead-band around a crossover point. Prevents the active clip pair from flickering when RPM hovers near a boundary. Try 50-200 RPM."));
                    EditorGUILayout.PropertyField(pairHoldCyclesProp, new GUIContent("Pair Hold Cycles", "Number of combustion cycles to hold a clip pair before allowing a switch. High values (e.g. 16) cause long pitch stretch if RPM races ahead of the held pair. Prefer 0.5–2."));
                    EditorGUILayout.PropertyField(maxPitchRatioBeyondPairProp, new GUIContent("Max Pitch Ratio Beyond Pair", "How far live RPM may go past the active pair before pitch is soft-clamped and hold is escaped (1.2 = ±20%). Prevents chipmunk pitch during catch-up."));
                    EditorGUILayout.PropertyField(loadVolumeAccChangerFactorProp, new GUIContent("Acc Volume Sensitivity", "How strongly load amplifies the acceleration bank volume. 1 = full scaling; 0 = flat."));
                    EditorGUILayout.PropertyField(loadVolumeDccChangerFactorProp, new GUIContent("Dec Volume Sensitivity", "How strongly load (inverted) amplifies the deceleration bank volume."));
                    EditorGUILayout.PropertyField(loadVolumeChangerMinValueProp, new GUIContent("Min Load Volume", "Minimum volume multiplier that the load-scaling can reach. Prevents clips from going completely silent."));
                    EditorGUILayout.PropertyField(maxVolumeAccProp, new GUIContent("Max Acc Volume", "Hard ceiling on acceleration bank volume. Useful when clips peak loudly at full throttle."));
                    EditorGUILayout.PropertyField(maxVolumeDccProp, new GUIContent("Max Dec Volume", "Hard ceiling on deceleration bank volume."));
                }
                DrawLoadCrossfadePreview();
            });

            DrawFoldoutSection(ref showCombustion, "Combustion Timing", () =>
            {
                EditorGUILayout.HelpBox("These settings simulate the rhythm of combustion events. They affect blend hold timing. Match them to your engine type for most accurate results.", MessageType.Info);
                EditorGUILayout.PropertyField(cylinderCountProp, new GUIContent("Cylinder Count", "Number of cylinders in the engine. A V8 = 8, inline-4 = 4. This affects the firing frequency used in the blend hold calculation."));
                EditorGUILayout.PropertyField(combustionCycleModeProp, new GUIContent("Combustion Cycle", "Four-stroke engines fire once every two revolutions (most car engines). Two-stroke engines fire every revolution. When in doubt, use Four-Stroke."));
                DrawCombustionPreview();
            });

            DrawFoldoutSection(ref showAccBank, "Acceleration Bank", () =>
            {
                EditorGUILayout.HelpBox("Add your engine recordings captured under THROTTLE (acceleration). Each clip should be recorded at a specific RPM - enter that RPM in the RPM field. The system blends between clips automatically.", MessageType.Info);
                if (acceleratingList != null) acceleratingList.DoLayoutList();
                EditorGUILayout.Space(6f);
                DrawBlendVisualization(acceleratingSoundsProp, true);
            });

            DrawFoldoutSection(ref showDecBank, "Deceleration Bank", () =>
            {
                EditorGUILayout.HelpBox(
                    "Optional closed-throttle (engine braking / overrun) clips. Leave empty to use the acceleration bank only — VNS automatically enables non-decelerate mode when this list is empty.\n" +
                    "If you add clips, match RPM points to the acceleration bank for the cleanest load crossfade.",
                    MessageType.Info);
                if (deceleratingList != null) deceleratingList.DoLayoutList();
                if (deceleratingSoundsProp != null && deceleratingSoundsProp.arraySize == 0)
                    EditorGUILayout.HelpBox("No deceleration clips — acceleration bank covers both throttle and lift-off.", MessageType.None);
                EditorGUILayout.Space(6f);
                DrawBlendVisualization(deceleratingSoundsProp, false);
            });

            DrawFoldoutSection(ref showTuning, "Tuning", () =>
            {
                EditorGUILayout.PropertyField(maximumTheoricalRPMProp, new GUIContent("Max Theoretical RPM", "The redline / rev limiter RPM of the engine. Used to normalise visualisations and pitch calculations. Set this to match your vehicle's actual rev limit."));
                if (advancedMode)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox("Pitch trims and sound-character strengths. Leave strengths at 0 to skip those DSP components entirely (better performance).", MessageType.Info);
                    EditorGUILayout.PropertyField(acPitchTrimProp, new GUIContent("Acc Pitch Trim", "Global pitch offset added to all acceleration clips. Positive = higher pitch. Use tiny values (±0.05) to nudge the overall engine tone under throttle."));
                    EditorGUILayout.PropertyField(dcPitchTrimProp, new GUIContent("Dec Pitch Trim", "Global pitch offset added to all deceleration clips. Use tiny values (±0.05) to adjust the engine tone on lift-off."));
                    EditorGUILayout.PropertyField(lowPassStrengthProp, new GUIContent("Low-Pass Strength", "How strongly a low-pass filter is applied at idle / low RPM, making the engine sound muffled. 0 = off (component not created). Typical range: 0.1-0.5."));
                    EditorGUILayout.PropertyField(highPassStrengthProp, new GUIContent("High-Pass Strength", "How strongly a high-pass filter is applied. Removes bass rumble at high RPM. 0 = off (component not created)."));
                    EditorGUILayout.PropertyField(resonanceStrengthProp, new GUIContent("Filter Resonance", "Adds a peak/resonance to the filter cutoff frequency, creating a nasal or growling quality. Keep below 0.3 to avoid harsh artefacts."));
                    EditorGUILayout.PropertyField(distortionStrengthProp, new GUIContent("Distortion Strength", "Boosts distortion character. Distortion also needs Distortion Intensity > 0 under FX Curves."));
                    EditorGUILayout.PropertyField(chorusStrengthProp, new GUIContent("Chorus Strength", "Adds a subtle pitch-wobble / chorus effect. 0 = off (component not created). Keep below 0.2."));
                    EditorGUILayout.PropertyField(reverbStrengthProp, new GUIContent("Reverb Strength", "Adds engine room resonance / reverb. 0 = off."));
                    EditorGUILayout.PropertyField(useSharedMixerReverbProp, new GUIContent("Use Mixer Reverb", "Instead of a per-source reverb filter, drives a reverb parameter on your AudioMixer. Use this for better performance when many vehicles share one mixer reverb bus."));
                    if (useSharedMixerReverbProp.boolValue) EditorGUILayout.PropertyField(reverbMixerParamNameProp, new GUIContent("Mixer Reverb Param", "The exact name of the exposed AudioMixer parameter to drive. Must match the name in your AudioMixer's Exposed Parameters list."));
                }
            });

            if (advancedMode)
            {
                DrawFoldoutSection(ref showFx, "FX Curves", () =>
                {
                    EditorGUILayout.HelpBox("Curve-driven FX - each curve's X axis is normalised RPM (0=idle, 1=redline). The Y axis controls the effect intensity at that RPM. Distortion Intensity 0 skips distortion components.", MessageType.Info);
                    EditorGUILayout.PropertyField(distortionCurveProp, new GUIContent("Distortion Curve", "Controls distortion amount vs RPM. Flat at 0 = no distortion; ramp up toward redline for a gritty high-RPM character."));
                    EditorGUILayout.PropertyField(distortionIntensityProp, new GUIContent("Distortion Intensity", "Master multiplier for the distortion curve. 1 = full curve range; 0 = disabled (no distortion component)."));
                    EditorGUILayout.PropertyField(mufflingIntensityProp, new GUIContent("Muffling Intensity", "How strongly the low-pass muffling filter is applied at idle. Higher values make the engine sound more enclosed at low RPM."));
                    EditorGUILayout.PropertyField(lowPassCurveProp, new GUIContent("Low-Pass Curve", "Controls the low-pass filter cutoff vs RPM. A curve that rises with RPM opens the filter, making high-RPM sounds brighter."));
                    EditorGUILayout.PropertyField(lowPassIntensityProp, new GUIContent("Low-Pass Intensity", "Master multiplier for the low-pass curve. 1 = full effect; 0 = filter fully open at all RPMs."));
                });

                DrawFoldoutSection(ref showFilterSlew, "Filter Slew Rates", () =>
                {
                    EditorGUILayout.HelpBox("How fast filter parameters may move per physics tick. Rarely needs changes.", MessageType.Info);
                    EditorGUILayout.PropertyField(filterLPFSlewHzProp, new GUIContent("LPF Slew (Hz)", "Max Hz/tick the low-pass cutoff may move."));
                    EditorGUILayout.PropertyField(filterHPFSlewHzProp, new GUIContent("HPF Slew (Hz)", "Max Hz/tick the high-pass cutoff may move."));
                    EditorGUILayout.PropertyField(filterReverbSlewDbSProp, new GUIContent("Reverb Slew (dB)", "Max dB/tick reverb level may move."));
                    EditorGUILayout.PropertyField(filterParamSlewRateProp, new GUIContent("Param Slew Rate", "Slew multiplier for 0–1 parameters (Q, distortion, chorus)."));
                });
            }

            DrawFoldoutSection(ref showBurble, "Exhaust Burble", () =>
            {
                EditorGUILayout.PropertyField(enableExhaustBurbleProp, new GUIContent("Enable Exhaust Burble", "Plays random one-shot exhaust crackle clips when you lift off the throttle at high RPM - the classic sports-car burble effect."));
                if (enableExhaustBurbleProp.boolValue)
                {
                    EditorGUILayout.HelpBox("Burble triggers when you lift off the throttle (low load) above the minimum RPM. Add 2-5 short exhaust pop clips for best results.", MessageType.Info);
                    EditorGUILayout.PropertyField(burbleSoundsProp, new GUIContent("Burble Clips", "One-shot audio clips for the exhaust crackle. Add short (0.05-0.3 s) exhaust pop sounds here."), true);
                    EditorGUILayout.PropertyField(burbleVolumeProp, new GUIContent("Burble Volume", "Master volume for the burble effect. 0.5-1.0 works well for most vehicles."));
                    EditorGUILayout.PropertyField(burbleMinRPMProp, new GUIContent("Min RPM to Trigger", "Burble will not play below this RPM. Set to roughly 60-70 % of your maximum RPM."));
                    if (advancedMode)
                    {
                        EditorGUILayout.PropertyField(burbleLoadLowThresholdProp, new GUIContent("Load Low Threshold", "Burble only triggers when load (throttle) is BELOW this value. Keep it low (0.1-0.2) so it only fires on full lift-off."));
                        EditorGUILayout.PropertyField(burbleLoadHighThresholdProp, new GUIContent("Load High Threshold", "Burble only triggers when load is ABOVE this value at the moment of lift-off. Prevents burble at very low throttle positions."));
                        EditorGUILayout.PropertyField(burbleRPMDropThresholdProp, new GUIContent("RPM Drop Threshold", "How fast RPM must be falling (RPM/s) to allow a burble. Prevents pops during steady deceleration."));
                        EditorGUILayout.PropertyField(burbleProbabilityProp, new GUIContent("Fire Probability", "Chance (0-1) that a burble fires each cycle. 0.6-0.8 gives a natural, irregular rhythm."));
                        EditorGUILayout.PropertyField(minBurbleDelayProp, new GUIContent("Min Delay (s)", "Minimum seconds between consecutive burble pops. Prevents machine-gun repetition."));
                        EditorGUILayout.PropertyField(maxBurbleDelayProp, new GUIContent("Max Delay (s)", "Maximum seconds between consecutive burble pops."));
                        EditorGUILayout.PropertyField(burbleRandomPitchVariationProp, new GUIContent("Pitch Variation", "Random ± pitch offset on each pop. A small value (0.05-0.15) keeps the sound organic."));
                        EditorGUILayout.PropertyField(burbleFadeRateProp, new GUIContent("Fade Rate", "How quickly each burble clip fades out after playing. Higher = shorter tail."));
                    }
                }
            });

            DrawFoldoutSection(ref showDctBurble, "DCT Shift Burble", () =>
            {
                EditorGUILayout.HelpBox("A short looped exhaust overlay that fires on gear-change events - mimicking the characteristic sound of a dual-clutch transmission shifting under load.", MessageType.Info);
                EditorGUILayout.PropertyField(enableDctShiftBurbleProp, new GUIContent("Enable DCT Shift Burble", "When enabled, a brief exhaust sound plays each time a gear-shift event is detected."));
                if (enableDctShiftBurbleProp.boolValue)
                {
                    EditorGUILayout.PropertyField(dctShiftBurbleSoundProp, new GUIContent("Shift Sound Clip", "The looped audio clip played during a DCT shift event. Use a short (0.1-0.3 s) exhaust blip sound."));
                    EditorGUILayout.PropertyField(dctShiftBurbleVolumeProp, new GUIContent("Shift Volume", "Volume of the shift burble. 0.5-0.9 is typical."));
                    if (advancedMode)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel(new GUIContent("RPM Vol Influence", "0 = constant volume; 1 = scales with RPM."));
                        int curInfl = Mathf.RoundToInt(dctShiftBurbleRpmVolumeInfluenceProp.floatValue);
                        int newInfl = EditorGUILayout.IntSlider(curInfl, 0, 1);
                        if (newInfl != curInfl) dctShiftBurbleRpmVolumeInfluenceProp.floatValue = newInfl;
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.PropertyField(dctShiftBurbleMinRPMProp, new GUIContent("Min RPM to Trigger", "Shift burble will not play below this RPM."));
                        EditorGUILayout.PropertyField(dctShiftBurbleMaxDurationProp, new GUIContent("Max Duration (s)", "Maximum time the shift sound may play. Keep short - 0.08-0.2 s is realistic."));
                        EditorGUILayout.PropertyField(dctShiftBurbleBasePitchProp, new GUIContent("Base Pitch", "Base playback pitch of the shift clip. 1.0 = original pitch."));
                        EditorGUILayout.PropertyField(dctShiftBurblePitchVariationProp, new GUIContent("Pitch Variation", "Random ± pitch offset each shift."));
                    }
                }
            });

            DrawFoldoutSection(ref showThrottleBody, "Throttle Body", () =>
            {
                EditorGUILayout.PropertyField(enableThrottleBodyProp, new GUIContent("Enable Throttle Body", "Simulates the sound of the throttle plate opening and closing - the 'whoosh' when you floor it (Intake Roar) and the flutter when you lift off at high RPM."));
                if (enableThrottleBodyProp.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "Intake Roar fires on tip-in; Throttle Flutter on tip-out.\n" +
                        "Detection is done by the input integration script (e.g. NWH adapter), which calls OnThrottleTipIn / OnThrottleTipOut.",
                        MessageType.Info);
                    EditorGUILayout.PropertyField(intakeRoarSoundsProp, new GUIContent("Intake Roar Clips", "Short tip-in whoosh / induction clips."), true);
                    EditorGUILayout.PropertyField(intakeRoarVolumeProp, new GUIContent("Intake Roar Volume", "Volume of the intake roar on tip-in."));
                    EditorGUILayout.PropertyField(throttleFlutterSoundsProp, new GUIContent("Flutter Clips", "Short tip-out flutter clips."), true);
                    EditorGUILayout.PropertyField(throttleFlutterVolumeProp, new GUIContent("Flutter Volume", "Volume of the throttle flutter sound on tip-out."));
                    if (advancedMode)
                    {
                        EditorGUILayout.PropertyField(throttleBodyPitchVariationProp, new GUIContent("Pitch Variation", "Random ± pitch shift on each throttle body event."));
                        EditorGUILayout.PropertyField(throttleBodyCooldownProp, new GUIContent("Cooldown (s)", "Minimum time between successive throttle body events."));
                    }
                }
            });

            DrawFoldoutSection(ref showRedline, "Exhaust Redline", () =>
            {
                EditorGUILayout.PropertyField(enableRedlineEffectProp, new GUIContent("Enable Redline Effect", "Plays looping exhaust crackle clips when the engine enters the redline RPM window."));
                if (enableRedlineEffectProp.boolValue)
                {
                    EditorGUILayout.PropertyField(redlineSoundsProp, new GUIContent("Redline Clips", "Short exhaust pop/crackle clips."), true);
                    EditorGUILayout.PropertyField(redlineVolumeProp, new GUIContent("Redline Volume", "Volume of the redline effect."));
                    EditorGUILayout.PropertyField(redlineMinRPMProp, new GUIContent("Min RPM", "RPM at which the redline effect begins."));
                    if (advancedMode)
                    {
                        EditorGUILayout.PropertyField(redlineMaxRPMProp, new GUIContent("Max RPM", "RPM ceiling for the redline effect (0 = use max RPM)."));
                        EditorGUILayout.PropertyField(redlineMinDelayProp, new GUIContent("Min Delay (s)", "Minimum seconds between redline pops."));
                        EditorGUILayout.PropertyField(redlineMaxDelayProp, new GUIContent("Max Delay (s)", "Maximum seconds between redline pops."));
                        EditorGUILayout.PropertyField(redlineBasePitchProp, new GUIContent("Base Pitch", "Base playback pitch of each redline clip."));
                        EditorGUILayout.PropertyField(redlinePitchVariationProp, new GUIContent("Pitch Variation", "Random ± pitch offset on each clip."));
                    }
                }
            });

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Simple mode: expand Core, Acceleration Bank, Tuning; collapse every other section.
        /// Does not remove or reorder sections — only open/closed state.
        /// </summary>
        private void ApplySimpleModeFoldouts()
        {
            showCore = true;
            showAccBank = true;
            showTuning = true;
            showDebug = false;
            showCurves = false;
            showBlend = false;
            showCombustion = false;
            showDecBank = false;
            showFx = false;
            showFilterSlew = false;
            showBurble = false;
            showDctBurble = false;
            showThrottleBody = false;
            showRedline = false;
        }

        private void DrawTopBar()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 72f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BgColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(1f, 1f, 1f, 0.07f));
            EditorGUI.LabelField(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 22f), "Vehicle Noise Synthesizer", headerStyle);
            EditorGUI.LabelField(new Rect(rect.x + 12f, rect.y + 28f, rect.width - 140f, 16f),
                "v1.9  •  github.com/ImDanOush/VehicleNoiseSynthesizer", mutedLabelStyle);

            // Simple / Advanced inspector mode (persisted)
            Rect modeRect = new Rect(rect.x + 12f, rect.y + 48f, Mathf.Min(280f, rect.width - 24f), 18f);
            int mode = advancedMode ? 1 : 0;
            int newMode = GUI.Toolbar(modeRect, mode, new[] { "Simple", "Advanced" });
            if (newMode != mode)
            {
                advancedMode = newMode == 1;
                EditorPrefs.SetBool(AdvancedPrefKey, advancedMode);
                if (!advancedMode)
                    ApplySimpleModeFoldouts();
            }
        }

        private void DrawLoadCrossfadePreview()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 78f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, PanelAltColor);
            float crossover = loadCrossoverPointProp.floatValue;
            float width = Mathf.Max(0.001f, loadBlendWidthProp.floatValue);
            float left = Mathf.Clamp01(crossover - width * 0.5f);
            float right = Mathf.Clamp01(crossover + width * 0.5f);
            Rect chart = new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 44f);
            DrawMiniGrid(chart, 4, 2);
            DrawCurve(chart, t => { float v = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(left, right, t)); return 1f - v; }, AccColor);
            DrawCurve(chart, t => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(left, right, t)), DecColor);
            float x = Mathf.Lerp(chart.x, chart.xMax, crossover);
            EditorGUI.DrawRect(new Rect(x, chart.y, 1f, chart.height), SelectedColor);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 57f, rect.width - 24f, 18f), "Acceleration / deceleration bank crossfade preview", mutedLabelStyle);
        }

        private void DrawCombustionPreview()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 88f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, PanelAltColor);
            Rect chart = new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 40f);
            DrawMiniGrid(chart, 6, 2);
            float maxRpm = Mathf.Max(1000f, maximumTheoricalRPMProp.floatValue);
            float cylinders = Mathf.Max(1, cylinderCountProp.intValue);
            bool fourStroke = combustionCycleModeProp.enumValueIndex == (int)VehicleNoiseSynthesizer.CombustionCycleMode.FourStroke;
            float eventsPerRev = fourStroke ? cylinders * 0.5f : cylinders;
            DrawCurve(chart, t => { float rpm = Mathf.Lerp(0f, maxRpm, t); float hz = (rpm / 60f) * eventsPerRev; float refHz = Mathf.Max(1f, (maxRpm / 60f) * eventsPerRev); return Mathf.Clamp01(hz / refHz); }, SelectedColor);
            float sampleRpm = ResolveCurrentRpm(maxRpm);
            float combustionHz = (sampleRpm / 60f) * eventsPerRev;
            float holdCycles = Mathf.Max(0f, pairHoldCyclesProp.floatValue);
            float holdTime = combustionHz > 0.001f ? holdCycles / combustionHz : 0f;
            GUI.Label(new Rect(rect.x + 12f, rect.y + 54f, rect.width - 24f, 16f),
                $"Combustion events/rev: {eventsPerRev:0.##}   ·   Current fire freq: {combustionHz:0.0} Hz   ·   Pair hold: {holdTime * 1000f:0.0} ms", mutedLabelStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 70f, rect.width - 24f, 16f),
                "Higher RPM raises firing frequency, so the pair-hold window naturally shortens.", mutedLabelStyle);
        }

        private void DrawBlendVisualization(SerializedProperty bankProp, bool isAcc)
        {
            int count = bankProp.arraySize;
            if (count == 0) { EditorGUILayout.HelpBox("No clips in this bank.  Add clips above to see the blend preview.", MessageType.Info); return; }

            float maxRpm = Mathf.Max(1000f, maximumTheoricalRPMProp.floatValue);
            Rect totalRect = GUILayoutUtility.GetRect(0f, TimelineHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(totalRect, BgColor);

            Rect volRect = new Rect(totalRect.x + 44f, totalRect.y + 6f, totalRect.width - 52f, totalRect.height - 22f);
            GUI.Label(new Rect(totalRect.x + 2f, volRect.y, 38f, 14f), "Volume", mutedLabelStyle);

            float currentRpm = ResolveCurrentRpm(maxRpm);

            int[] sortedToSource = new int[count];
            float[] sortedRpms = new float[count];
            for (int i = 0; i < count; i++)
            {
                sortedToSource[i] = i;
                sortedRpms[i] = Mathf.Max(1f, bankProp.GetArrayElementAtIndex(i).FindPropertyRelative("rpmValue").intValue);
            }
            Array.Sort(sortedRpms, sortedToSource);

            int activeSrcLo = -1, activeSrcHi = -1;
            if (currentRpm < sortedRpms[0])
            {
                activeSrcLo = activeSrcHi = sortedToSource[0];
            }
            else if (currentRpm > sortedRpms[count - 1])
            {
                activeSrcLo = activeSrcHi = sortedToSource[count - 1];
            }
            else
            {
                for (int si = 0; si < count - 1; si++)
                {
                    if (currentRpm >= sortedRpms[si] && currentRpm <= sortedRpms[si + 1])
                    {
                        activeSrcLo = sortedToSource[si];
                        activeSrcHi = sortedToSource[si + 1];
                        break;
                    }
                }
                if (activeSrcLo < 0) { activeSrcLo = activeSrcHi = sortedToSource[count - 1]; }
            }

            Color[] colors = GenerateColors(count, isAcc);

            const int envSamples = 200;
            float[] envelopeY = new float[envSamples];
            for (int s = 0; s < envSamples; s++) envelopeY[s] = 0f;

            for (int si = 0; si < count - 1; si++)
            {
                float loRef = sortedRpms[si];
                float hiRef = sortedRpms[si + 1];
                for (int s = 0; s < envSamples; s++)
                {
                    float sampleRpm = Mathf.Lerp(0f, maxRpm, s / (float)(envSamples - 1));
                    if (sampleRpm < loRef || sampleRpm > hiRef) continue;
                    float t = Mathf.InverseLerp(loRef, hiRef, sampleRpm);
                    float angle = t * Mathf.PI * 0.5f;
                    float sum = Mathf.Cos(angle) + Mathf.Sin(angle);
                    if (sum > envelopeY[s]) envelopeY[s] = sum;
                }
            }

            var clipDrawData = new (Vector3[] curve, int srcIdx)[count];
            for (int si = 0; si < count; si++)
            {
                float prevRpm = si > 0 ? sortedRpms[si - 1] : 0f;
                float refRpm = sortedRpms[si];
                float nextRpm = si < count - 1 ? sortedRpms[si + 1] : maxRpm;
                int srcIdx = sortedToSource[si];
                clipDrawData[si] = (BuildCosCurve(volRect, prevRpm, refRpm, nextRpm, maxRpm), srcIdx);
            }

            Handles.BeginGUI();

            Color envFill = isAcc ? new Color(0.23f, 0.76f, 1f, 0.14f) : new Color(1f, 0.57f, 0.22f, 0.14f);
            Handles.color = envFill;
            for (int s = 0; s < envSamples - 1; s++)
            {
                float x0 = Mathf.Lerp(volRect.x, volRect.xMax, s / (float)(envSamples - 1));
                float x1 = Mathf.Lerp(volRect.x, volRect.xMax, (s + 1) / (float)(envSamples - 1));
                float y0 = WeightToY(envelopeY[s], volRect);
                float y1 = WeightToY(envelopeY[s + 1], volRect);
                Handles.DrawAAConvexPolygon(
                    new Vector3(x0, y0), new Vector3(x1, y1),
                    new Vector3(x1, volRect.yMax), new Vector3(x0, volRect.yMax));
            }

            for (int si = 0; si < clipDrawData.Length; si++)
            {
                int srcIdx = clipDrawData[si].srcIdx;
                bool isActive = (srcIdx == activeSrcLo || srcIdx == activeSrcHi);
                float alpha = isActive ? 0.90f : 0.13f;
                float thickness = isActive ? 2.5f : 1.0f;
                Handles.color = new Color(colors[srcIdx].r, colors[srcIdx].g, colors[srcIdx].b, alpha);
                Handles.DrawAAPolyLine(thickness, clipDrawData[si].curve);
            }

            Handles.EndGUI();

            DrawRpmScale(totalRect, maxRpm);

            float mx = Mathf.Lerp(totalRect.x + 44f, totalRect.xMax - 4f, Mathf.Clamp01(currentRpm / maxRpm));
            EditorGUI.DrawRect(new Rect(mx, totalRect.y, 1.5f, totalRect.height), new Color(1f, 1f, 1f, 0.5f));
            GUI.Label(new Rect(mx - 30f, totalRect.y, 60f, 14f), Mathf.RoundToInt(currentRpm).ToString(), new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });

            string pairLabel = activeSrcLo == activeSrcHi
                ? $"Active: clip {activeSrcLo}  ({Mathf.RoundToInt(bankProp.GetArrayElementAtIndex(activeSrcLo).FindPropertyRelative("rpmValue").intValue)} RPM)"
                : $"Active: clips {activeSrcLo}-{activeSrcHi}  ({Mathf.RoundToInt(bankProp.GetArrayElementAtIndex(activeSrcLo).FindPropertyRelative("rpmValue").intValue)}-{Mathf.RoundToInt(bankProp.GetArrayElementAtIndex(activeSrcHi).FindPropertyRelative("rpmValue").intValue)} RPM)";
            GUI.Label(new Rect(totalRect.x + 2f, totalRect.yMax - 16f, totalRect.width - 4f, 14f), pairLabel, mutedLabelStyle);
        }

        /// <summary>Resolves the current RPM for visualization markers, respecting debug mode in both edit and play states.</summary>
        private float ResolveCurrentRpm(float maxRpm)
        {
            if (synth == null) return maxRpm * 0.35f;

            bool isPlaying = Application.isPlaying;
            if (isPlaying)
                return synth.debug ? synth.debugrpm : synth.rpm;
            else
                return synth.debug ? synth.debugrpm : maxRpm * 0.35f;
        }

        private Vector3[] BuildCosCurve(Rect rect, float leftEdge, float center, float rightEdge, float maxRpm)
        {
            int n = 40;
            Vector3[] pts = new Vector3[n * 2 - 1];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float rpm = Mathf.Lerp(leftEdge, center, t);
                float angle = t * Mathf.PI * 0.5f;
                float w = Mathf.Sin(angle);
                pts[i] = new Vector3(RpmToX(rpm, rect, maxRpm), WeightToY(w, rect), 0f);
            }
            for (int i = 1; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float rpm = Mathf.Lerp(center, rightEdge, t);
                float angle = t * Mathf.PI * 0.5f;
                float w = Mathf.Cos(angle);
                pts[n - 1 + i] = new Vector3(RpmToX(rpm, rect, maxRpm), WeightToY(w, rect), 0f);
            }
            return pts;
        }

        private void DrawRpmScale(Rect totalRect, float maxRpm)
        {
            GUIStyle s = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.42f) } };
            Rect plotRect = new Rect(totalRect.x + 44f, totalRect.y, totalRect.width - 52f, totalRect.height);
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f;
                float rpm = Mathf.Lerp(0f, maxRpm, t);
                float x = Mathf.Lerp(plotRect.x, plotRect.xMax, t);
                GUI.Label(new Rect(x - 24f, totalRect.yMax - 14f, 48f, 14f), Mathf.RoundToInt(rpm).ToString(), s);
            }
        }

        private float RpmToX(float rpm, Rect r, float max) => Mathf.Lerp(r.x, r.xMax, Mathf.Clamp01(rpm / max));
        private float WeightToY(float w, Rect r) => Mathf.Lerp(r.yMax - 2f, r.y + 6f, Mathf.Clamp01(w));

        private Color[] GenerateColors(int count, bool isAcc)
        {
            Color[] c = new Color[count];
            float hBase = isAcc ? 0.53f : 0.06f;
            for (int i = 0; i < count; i++)
                c[i] = Color.HSVToRGB(Mathf.Repeat(hBase + (count <= 1 ? 0f : i / (count - 1f) * 0.18f), 1f), 0.75f, 0.9f);
            return c;
        }

        private string Shorten(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 1) + "\u2026";

        private void DrawFoldoutSection(ref bool foldout, string title, Action drawBody)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Rect headerRect = GUILayoutUtility.GetRect(20f, 24f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, PanelColor);
            foldout = EditorGUI.Foldout(new Rect(headerRect.x + 8f, headerRect.y + 3f, headerRect.width - 16f, 18f), foldout, title, true, subHeaderStyle);
            if (foldout) { EditorGUILayout.Space(4f); drawBody?.Invoke(); }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }

        private void DrawMiniGrid(Rect rect, int vLines, int hLines)
        {
            Handles.BeginGUI();
            Handles.color = GridColor;
            for (int i = 0; i <= vLines; i++) { float x = Mathf.Lerp(rect.x, rect.xMax, i / (float)vLines); Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.yMax)); }
            for (int i = 0; i <= hLines; i++) { float y = Mathf.Lerp(rect.y, rect.yMax, i / (float)hLines); Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.xMax, y)); }
            Handles.EndGUI();
        }

        private void DrawCurve(Rect rect, Func<float, float> eval, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Vector3[] pts = new Vector3[64];
            for (int i = 0; i < pts.Length; i++) { float t = i / (float)(pts.Length - 1); float v = Mathf.Clamp01(eval(t)); pts[i] = new Vector3(Mathf.Lerp(rect.x, rect.xMax, t), Mathf.Lerp(rect.yMax, rect.y, v), 0f); }
            Handles.DrawAAPolyLine(2f, pts);
            Handles.EndGUI();
        }

        private const float ClipRowH = 20f;
        private const float ClipElementH = 64f;

        private void RefreshClipLists()
        {
            acceleratingList = BuildClipList(acceleratingSoundsProp, "Acceleration Clips", _accExpanded);
            deceleratingList = BuildClipList(deceleratingSoundsProp, "Deceleration Clips", _decExpanded);
        }

        private ReorderableList BuildClipList(SerializedProperty prop, string label, Dictionary<int, bool> expandedMap)
        {
            var list = new ReorderableList(serializedObject, prop, true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r, label),
                elementHeightCallback = i =>
                {
                    bool exp = expandedMap != null && expandedMap.TryGetValue(i, out bool v) && v;
                    return exp ? ClipRowH * 5f + 12f : ClipRowH * 2f + 6f;
                },
                onAddCallback = l =>
                {
                    ReorderableList.defaultBehaviours.DoAddButton(l);
                    int newIndex = l.serializedProperty.arraySize - 1;
                    var el = l.serializedProperty.GetArrayElementAtIndex(newIndex);
                    el.FindPropertyRelative("audioClip").objectReferenceValue = null;
                    el.FindPropertyRelative("rpmValue").intValue = 1000;
                    el.FindPropertyRelative("volumeOffset").floatValue = 0f;
                    el.FindPropertyRelative("pitchOffset").floatValue = 0f;
                    el.FindPropertyRelative("loPitch").floatValue = 1f;
                    el.FindPropertyRelative("hiPitch").floatValue = 1f;
                    el.FindPropertyRelative("description").stringValue = "";
                },
                onRemoveCallback = l =>
                {
                    ReorderableList.defaultBehaviours.DoRemoveButton(l);
                }
            };

            list.drawElementCallback = (rect, index, active, focused) =>
            {
                var el = list.serializedProperty.GetArrayElementAtIndex(index);
                var clipP = el.FindPropertyRelative("audioClip");
                var rpmP = el.FindPropertyRelative("rpmValue");
                var loP = el.FindPropertyRelative("loPitch");
                var hiP = el.FindPropertyRelative("hiPitch");

                rect.y += 2f;
                float w = rect.width;
                float r1y = rect.y;
                float r2y = rect.y + ClipRowH + 2f;
                float fldH = EditorGUIUtility.singleLineHeight;

                // Row 1: clip + RPM + expand toggle
                EditorGUI.PropertyField(new Rect(rect.x, r1y, w * 0.42f, fldH), clipP, GUIContent.none);

                Rect rpmLabelR = new Rect(rect.x + w * 0.43f, r1y, 30f, fldH);
                Rect rpmFieldR = new Rect(rpmLabelR.xMax + 2f, r1y, 56f, fldH);
                GUI.Label(rpmLabelR, "RPM", EditorStyles.miniLabel);
                int newRpm = EditorGUI.IntField(rpmFieldR, rpmP.intValue);
                if (newRpm != rpmP.intValue) rpmP.intValue = Mathf.Max(1, newRpm);

                // Expand toggle button at far right of Row 1
                bool currentlyExpanded = expandedMap != null && expandedMap.TryGetValue(index, out bool curExp) && curExp;
                Rect toggleR = new Rect(rect.xMax - 22f, r1y, 22f, fldH);
                bool newExpanded = GUI.Toggle(toggleR, currentlyExpanded, currentlyExpanded ? "▼" : "▶", EditorStyles.miniButton);
                if (newExpanded != currentlyExpanded && expandedMap != null)
                {
                    expandedMap[index] = newExpanded;
                }

                float maxRpm = Mathf.Max(1000f, maximumTheoricalRPMProp.floatValue);
                float currentRpm = ResolveCurrentRpm(maxRpm);
                float progress = Mathf.Clamp01(currentRpm / maxRpm);
                float hiLoMul = Mathf.Lerp(loP.floatValue, hiP.floatValue, progress);
                float previewPitch = Mathf.Clamp(currentRpm / Mathf.Max(1f, rpmP.intValue) * hiLoMul, 0.01f, 3f);
                Rect infoRect = new Rect(rect.x, r2y, w, fldH);
                GUI.Label(infoRect,
                    $"Pitch = RPM÷clipRPM×Hi/Lo  •  Preview {previewPitch:0.00}× at {currentRpm:0} RPM",
                    mutedLabelStyle);

                // Rows 3-5 - only when expanded
                bool isExpanded = expandedMap != null && expandedMap.TryGetValue(index, out bool expVal) && expVal;
                if (isExpanded)
                {
                    var volOffP = el.FindPropertyRelative("volumeOffset");
                    var pitchOffP = el.FindPropertyRelative("pitchOffset");
                    var descP = el.FindPropertyRelative("description");

                    float r3y = infoRect.y + ClipRowH + 2f;
                    float r4y = r3y + ClipRowH + 2f;
                    float r5y = r4y + ClipRowH + 2f;
                    float advFieldW = 72f;
                    float advLabelW = 38f;
                    float advSpacing = advLabelW + advFieldW + 6f;
                    float halfW = (w - 12f) * 0.5f;

                    // Row 3: Lo pitch | Hi pitch
                    Rect loLabelR = new Rect(rect.x, r3y, 18f, fldH);
                    Rect loFieldR = new Rect(loLabelR.xMax + 2f, r3y, halfW - 20f, fldH);
                    GUI.Label(loLabelR, new GUIContent("Lo", "Low-end pitch multiplier at minimum RPM. 1 = neutral. Raise above 1 to make the clip sound higher even at low RPM."), EditorStyles.miniLabel);
                    EditorGUI.Slider(loFieldR, loP, 0.01f, 3f, GUIContent.none);

                    Rect hiLabelR = new Rect(rect.x + halfW + 12f, r3y, 18f, fldH);
                    Rect hiFieldR = new Rect(hiLabelR.xMax + 2f, r3y, halfW - 20f, fldH);
                    GUI.Label(hiLabelR, new GUIContent("Hi", "High-end pitch multiplier at maximum RPM. 1 = neutral. Values above 1 add extra pitch rise toward redline."), EditorStyles.miniLabel);
                    EditorGUI.Slider(hiFieldR, hiP, 0.01f, 3f, GUIContent.none);

                    // Row 4: Vol+ | Pitch+
                    Rect volLabelR4 = new Rect(rect.x, r4y, advLabelW, fldH);
                    Rect volFieldR4 = new Rect(volLabelR4.xMax, r4y, advFieldW, fldH);
                    GUI.Label(volLabelR4, new GUIContent("Vol+", "Per-clip volume trim (−1 to +1). Use this to balance a clip that is louder or quieter than your other clips. 0 = no change."), EditorStyles.miniLabel);
                    EditorGUI.PropertyField(volFieldR4, volOffP, GUIContent.none);

                    Rect pitchLabelR4 = new Rect(rect.x + advSpacing, r4y, advLabelW, fldH);
                    Rect pitchFieldR4 = new Rect(pitchLabelR4.xMax, r4y, advFieldW, fldH);
                    GUI.Label(pitchLabelR4, new GUIContent("Pitch+", "Per-clip pitch offset added on top of the RPM-based pitch. Use tiny values (±0.05) to tune a clip that sounds slightly off. 0 = no change."), EditorStyles.miniLabel);
                    EditorGUI.PropertyField(pitchFieldR4, pitchOffP, GUIContent.none);

                    // Row 5: Note (description)
                    Rect noteLabelR5 = new Rect(rect.x, r5y, advLabelW, fldH);
                    Rect noteFieldR5 = new Rect(noteLabelR5.xMax, r5y, w - advLabelW, fldH);
                    GUI.Label(noteLabelR5, new GUIContent("Note:", "Optional text label for this clip - useful for remembering which RPM point or microphone position was used when recording."), EditorStyles.miniLabel);
                    EditorGUI.PropertyField(noteFieldR5, descP, GUIContent.none);
                }
            };

            return list;
        }

        private void EnsureStyles()
        {
            if (headerStyle != null) return;
            headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            subHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            mutedLabelStyle = new GUIStyle(EditorStyles.label) { fontSize = 10, normal = { textColor = TextMuted } };
            centeredMiniStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = TextMuted } };
        }

        private void CacheProperties()
        {
            debugProp = serializedObject.FindProperty("debug");
            debugRpmProp = serializedObject.FindProperty("debugrpm");
            debugLoadProp = serializedObject.FindProperty("debugload");
            enableDiagnosticLoggerProp = serializedObject.FindProperty("enableDiagnosticLogger");
            enableBurbleDiagnosticsProp = serializedObject.FindProperty("enableBurbleDiagnostics");
            audioSourceTemplateProp = serializedObject.FindProperty("audioSourceTemplate");
            mixerProp = serializedObject.FindProperty("mixer");
            mixerTypeProp = serializedObject.FindProperty("mixerType");
            masterVolumeProp = serializedObject.FindProperty("masterVolume");
            autoBlipProp = serializedObject.FindProperty("autoBlip");
            rpmDeviationProp = serializedObject.FindProperty("rpmdeviation");
            pitchCurveProp = serializedObject.FindProperty("pitchCurve");
            volumeCurveProp = serializedObject.FindProperty("volumeCurve");
            loadEffectivenessOnPitchProp = serializedObject.FindProperty("loadEffectivenessOnPitch");
            idleVolumeProp = serializedObject.FindProperty("idleVolume");
            keepBankClipsPlayingProp = serializedObject.FindProperty("keepBankClipsPlaying");
            clipVolumeResponseTimeProp = serializedObject.FindProperty("clipVolumeResponseTime");
            clipPitchResponseTimeProp = serializedObject.FindProperty("clipPitchResponseTime");
            rpmResponseTimeProp = serializedObject.FindProperty("rpmResponseTime");
            loadResponseTimeProp = serializedObject.FindProperty("loadResponseTime");
            pairHysteresisRpmProp = serializedObject.FindProperty("pairHysteresisRpm");
            pairHoldCyclesProp = serializedObject.FindProperty("pairHoldCycles");
            maxPitchRatioBeyondPairProp = serializedObject.FindProperty("maxPitchRatioBeyondPair");
            loadCrossoverPointProp = serializedObject.FindProperty("loadCrossoverPoint");
            loadBlendWidthProp = serializedObject.FindProperty("loadBlendWidth");
            loadVolumeAccChangerFactorProp = serializedObject.FindProperty("loadVolumeAccChangerFactor");
            loadVolumeDccChangerFactorProp = serializedObject.FindProperty("loadVolumeDccChangerFactor");
            loadVolumeChangerMinValueProp = serializedObject.FindProperty("loadVolumeChangerMinValue");
            maxVolumeAccProp = serializedObject.FindProperty("maxVolumeAcc");
            maxVolumeDccProp = serializedObject.FindProperty("maxVolumeDcc");
            cylinderCountProp = serializedObject.FindProperty("cylinderCount");
            combustionCycleModeProp = serializedObject.FindProperty("combustionCycleMode");
            acceleratingSoundsProp = serializedObject.FindProperty("acceleratingSounds");
            deceleratingSoundsProp = serializedObject.FindProperty("deceleratingSounds");
            acPitchTrimProp = serializedObject.FindProperty("acPitchTrim");
            dcPitchTrimProp = serializedObject.FindProperty("dcPitchTrim");
            maximumTheoricalRPMProp = serializedObject.FindProperty("maximumTheoricalRPM");
            lowPassStrengthProp = serializedObject.FindProperty("lowPassStrength");
            highPassStrengthProp = serializedObject.FindProperty("highPassStrength");
            resonanceStrengthProp = serializedObject.FindProperty("resonanceStrength");
            distortionStrengthProp = serializedObject.FindProperty("distortionStrength");
            chorusStrengthProp = serializedObject.FindProperty("chorusStrength");
            reverbStrengthProp = serializedObject.FindProperty("reverbStrength");
            useSharedMixerReverbProp = serializedObject.FindProperty("useSharedMixerReverb");
            reverbMixerParamNameProp = serializedObject.FindProperty("reverbMixerParamName");
            distortionCurveProp = serializedObject.FindProperty("distortionCurve");
            distortionIntensityProp = serializedObject.FindProperty("distortionIntensity");
            mufflingIntensityProp = serializedObject.FindProperty("mufflingIntensity");
            lowPassCurveProp = serializedObject.FindProperty("lowPassCurve");
            lowPassIntensityProp = serializedObject.FindProperty("lowPassIntensity");
            enableExhaustBurbleProp = serializedObject.FindProperty("enableExhaustBurble");
            burbleSoundsProp = serializedObject.FindProperty("burbleSounds");
            burbleVolumeProp = serializedObject.FindProperty("burbleVolume");
            burbleMinRPMProp = serializedObject.FindProperty("burbleMinRPM");
            burbleLoadLowThresholdProp = serializedObject.FindProperty("burbleLoadLowThreshold");
            burbleLoadHighThresholdProp = serializedObject.FindProperty("burbleLoadHighThreshold");
            burbleRPMDropThresholdProp = serializedObject.FindProperty("burbleRPMDropThreshold");
            burbleProbabilityProp = serializedObject.FindProperty("burbleProbability");
            minBurbleDelayProp = serializedObject.FindProperty("minBurbleDelay");
            maxBurbleDelayProp = serializedObject.FindProperty("maxBurbleDelay");
            burbleRandomPitchVariationProp = serializedObject.FindProperty("burbleRandomPitchVariation");
            burbleFadeRateProp = serializedObject.FindProperty("burbleFadeRate");
            enableDctShiftBurbleProp = serializedObject.FindProperty("enableDctShiftBurble");
            dctShiftBurbleSoundProp = serializedObject.FindProperty("dctShiftBurbleSound");
            dctShiftBurbleVolumeProp = serializedObject.FindProperty("dctShiftBurbleVolume");
            dctShiftBurbleRpmVolumeInfluenceProp = serializedObject.FindProperty("dctShiftBurbleRpmVolumeInfluence");
            dctShiftBurbleMinRPMProp = serializedObject.FindProperty("dctShiftBurbleMinRPM");
            dctShiftBurbleMaxDurationProp = serializedObject.FindProperty("dctShiftBurbleMaxDuration");
            dctShiftBurbleBasePitchProp = serializedObject.FindProperty("dctShiftBurbleBasePitch");
            dctShiftBurblePitchVariationProp = serializedObject.FindProperty("dctShiftBurblePitchVariation");
            // Throttle Body
            enableThrottleBodyProp = serializedObject.FindProperty("enableThrottleBody");
            intakeRoarSoundsProp = serializedObject.FindProperty("intakeRoarSounds");
            throttleFlutterSoundsProp = serializedObject.FindProperty("throttleFlutterSounds");
            intakeRoarVolumeProp = serializedObject.FindProperty("intakeRoarVolume");
            throttleFlutterVolumeProp = serializedObject.FindProperty("throttleFlutterVolume");
            throttleBodyPitchVariationProp = serializedObject.FindProperty("throttleBodyPitchVariation");
            throttleBodyCooldownProp = serializedObject.FindProperty("throttleBodyCooldown");

            // Redline
            enableRedlineEffectProp = serializedObject.FindProperty("enableRedlineEffect");
            redlineSoundsProp = serializedObject.FindProperty("redlineSounds");
            redlineVolumeProp = serializedObject.FindProperty("redlineVolume");
            redlineMinRPMProp = serializedObject.FindProperty("redlineMinRPM");
            redlineMaxRPMProp = serializedObject.FindProperty("redlineMaxRPM");
            redlineMinDelayProp = serializedObject.FindProperty("redlineMinDelay");
            redlineMaxDelayProp = serializedObject.FindProperty("redlineMaxDelay");
            redlineBasePitchProp = serializedObject.FindProperty("redlineBasePitch");
            redlinePitchVariationProp = serializedObject.FindProperty("redlinePitchVariation");

            filterLPFSlewHzProp = serializedObject.FindProperty("filterLPFSlewHz");
            filterHPFSlewHzProp = serializedObject.FindProperty("filterHPFSlewHz");
            filterReverbSlewDbSProp = serializedObject.FindProperty("filterReverbSlewDbS");
            filterParamSlewRateProp = serializedObject.FindProperty("filterParamSlewRate");
            enablePairSelectorDiagnosticsProp = serializedObject.FindProperty("enablePairSelectorDiagnostics");
        }
    }
}
#endif
