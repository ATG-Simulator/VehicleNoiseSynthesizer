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
        private SerializedProperty masterVolumeProp, targetedShiftPitchProp, autoBlipProp, rpmDeviationProp;
        private SerializedProperty pitchCurveProp, volumeCurveProp, loadEffectivenessOnPitchProp;
        private SerializedProperty idlePitchProp, idleVolumeProp;
        private SerializedProperty keepBankClipsPlayingProp, clipVolumeResponseTimeProp, clipPitchResponseTimeProp;
        private SerializedProperty rpmResponseTimeProp, loadResponseTimeProp;
        private SerializedProperty pairHysteresisRpmProp, pairHoldCyclesProp;
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
        private SerializedProperty burbleMinRPMProp, burbleLoadThresholdProp, burbleRPMDropThresholdProp;
        private SerializedProperty burbleProbabilityProp, minBurbleDelayProp, maxBurbleDelayProp;
        private SerializedProperty burbleRandomPitchVariationProp, burbleFadeRateProp;
        private SerializedProperty enableEngineLuggingProp, luggingSoundsProp, luggingVolumeProp;
        private SerializedProperty luggingMinRPMThresholdProp, luggingMaxRPMThresholdProp, luggingMinLoadThresholdProp;
        private SerializedProperty luggingFadeInSpeedProp, luggingFadeOutSpeedProp;
        private SerializedProperty luggingBasePitchProp, luggingRandomPitchVariationProp;

        private ReorderableList acceleratingList;
        private ReorderableList deceleratingList;

        private bool showDebug = true, showCore = true, showCurves = true, showBlend = true;
        private bool showCombustion = true, showAccBank = true, showDecBank = true;
        private bool showTuning = true, showFx, showBurble, showLugging;

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
            CacheProperties();
            EnsureStyles();
            acceleratingList = BuildClipList(acceleratingSoundsProp, "Acceleration Clips");
            deceleratingList = BuildClipList(deceleratingSoundsProp, "Deceleration Clips");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EnsureStyles();

            DrawTopBar();
            EditorGUILayout.Space(6f);

            DrawFoldoutSection(ref showDebug, "Debug", () =>
            {
                EditorGUILayout.PropertyField(debugProp);
                if (debugProp.boolValue) { EditorGUILayout.PropertyField(debugRpmProp); EditorGUILayout.PropertyField(debugLoadProp); }
                EditorGUILayout.PropertyField(enableDiagnosticLoggerProp);
                EditorGUILayout.PropertyField(enableBurbleDiagnosticsProp);
            });

            DrawFoldoutSection(ref showCore, "Core", () =>
            {
                EditorGUILayout.PropertyField(audioSourceTemplateProp);
                EditorGUILayout.PropertyField(mixerProp);
                EditorGUILayout.PropertyField(mixerTypeProp);
                EditorGUILayout.Slider(masterVolumeProp, 0.007f, 1f);
                EditorGUILayout.PropertyField(targetedShiftPitchProp);
                EditorGUILayout.PropertyField(autoBlipProp);
                EditorGUILayout.PropertyField(rpmDeviationProp);
            });

            DrawFoldoutSection(ref showCurves, "Global Curves", () =>
            {
                EditorGUILayout.PropertyField(pitchCurveProp);
                EditorGUILayout.PropertyField(volumeCurveProp);
                EditorGUILayout.PropertyField(loadEffectivenessOnPitchProp);
                EditorGUILayout.PropertyField(idlePitchProp);
                EditorGUILayout.PropertyField(idleVolumeProp);
            });

            DrawFoldoutSection(ref showBlend, "Blend Behaviour", () =>
            {
                EditorGUILayout.PropertyField(keepBankClipsPlayingProp);
                EditorGUILayout.PropertyField(clipVolumeResponseTimeProp);
                EditorGUILayout.PropertyField(clipPitchResponseTimeProp);
                EditorGUILayout.PropertyField(rpmResponseTimeProp);
                EditorGUILayout.PropertyField(loadResponseTimeProp);
                EditorGUILayout.PropertyField(pairHysteresisRpmProp);
                EditorGUILayout.PropertyField(pairHoldCyclesProp);
                EditorGUILayout.PropertyField(loadCrossoverPointProp);
                EditorGUILayout.PropertyField(loadBlendWidthProp);
                EditorGUILayout.PropertyField(loadVolumeAccChangerFactorProp);
                EditorGUILayout.PropertyField(loadVolumeDccChangerFactorProp);
                EditorGUILayout.PropertyField(loadVolumeChangerMinValueProp);
                EditorGUILayout.PropertyField(maxVolumeAccProp);
                EditorGUILayout.PropertyField(maxVolumeDccProp);
                DrawLoadCrossfadePreview();
            });

            DrawFoldoutSection(ref showCombustion, "Combustion Timing", () =>
            {
                EditorGUILayout.PropertyField(cylinderCountProp);
                EditorGUILayout.PropertyField(combustionCycleModeProp);
                DrawCombustionPreview();
            });

            DrawFoldoutSection(ref showAccBank, "Acceleration Bank", () =>
            {
                if (acceleratingList != null) acceleratingList.DoLayoutList();
                EditorGUILayout.Space(6f);
                DrawBlendVisualization(acceleratingSoundsProp, true);
            });

            DrawFoldoutSection(ref showDecBank, "Deceleration Bank", () =>
            {
                if (deceleratingList != null) deceleratingList.DoLayoutList();
                EditorGUILayout.Space(6f);
                DrawBlendVisualization(deceleratingSoundsProp, false);
            });

            DrawFoldoutSection(ref showTuning, "Tuning", () =>
            {
                EditorGUILayout.PropertyField(acPitchTrimProp);
                EditorGUILayout.PropertyField(dcPitchTrimProp);
                EditorGUILayout.PropertyField(maximumTheoricalRPMProp);
                EditorGUILayout.PropertyField(lowPassStrengthProp);
                EditorGUILayout.PropertyField(highPassStrengthProp);
                EditorGUILayout.PropertyField(resonanceStrengthProp);
                EditorGUILayout.PropertyField(distortionStrengthProp);
                EditorGUILayout.PropertyField(chorusStrengthProp);
                EditorGUILayout.PropertyField(reverbStrengthProp);
                EditorGUILayout.PropertyField(useSharedMixerReverbProp);
                if (useSharedMixerReverbProp.boolValue) EditorGUILayout.PropertyField(reverbMixerParamNameProp);
            });

            DrawFoldoutSection(ref showFx, "FX Curves", () =>
            {
                EditorGUILayout.PropertyField(distortionCurveProp);
                EditorGUILayout.PropertyField(distortionIntensityProp);
                EditorGUILayout.PropertyField(mufflingIntensityProp);
                EditorGUILayout.PropertyField(lowPassCurveProp);
                EditorGUILayout.PropertyField(lowPassIntensityProp);
            });

            DrawFoldoutSection(ref showBurble, "Burble", () =>
            {
                EditorGUILayout.PropertyField(enableExhaustBurbleProp);
                if (enableExhaustBurbleProp.boolValue)
                {
                    EditorGUILayout.PropertyField(burbleSoundsProp, true);
                    EditorGUILayout.PropertyField(burbleVolumeProp);
                    EditorGUILayout.PropertyField(burbleMinRPMProp);
                    EditorGUILayout.PropertyField(burbleLoadThresholdProp);
                    EditorGUILayout.PropertyField(burbleRPMDropThresholdProp);
                    EditorGUILayout.PropertyField(burbleProbabilityProp);
                    EditorGUILayout.PropertyField(minBurbleDelayProp);
                    EditorGUILayout.PropertyField(maxBurbleDelayProp);
                    EditorGUILayout.PropertyField(burbleRandomPitchVariationProp);
                    EditorGUILayout.PropertyField(burbleFadeRateProp);
                }
            });

            DrawFoldoutSection(ref showLugging, "Lugging", () =>
            {
                EditorGUILayout.PropertyField(enableEngineLuggingProp);
                if (enableEngineLuggingProp.boolValue)
                {
                    EditorGUILayout.PropertyField(luggingSoundsProp, true);
                    EditorGUILayout.PropertyField(luggingVolumeProp);
                    EditorGUILayout.PropertyField(luggingMinRPMThresholdProp);
                    EditorGUILayout.PropertyField(luggingMaxRPMThresholdProp);
                    EditorGUILayout.PropertyField(luggingMinLoadThresholdProp);
                    EditorGUILayout.PropertyField(luggingFadeInSpeedProp);
                    EditorGUILayout.PropertyField(luggingFadeOutSpeedProp);
                    EditorGUILayout.PropertyField(luggingBasePitchProp);
                    EditorGUILayout.PropertyField(luggingRandomPitchVariationProp);
                }
            });

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTopBar()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 56f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BgColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(1f, 1f, 1f, 0.07f));
            EditorGUI.LabelField(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 22f), "Vehicle Noise Synthesizer", headerStyle);
            EditorGUI.LabelField(new Rect(rect.x + 12f, rect.y + 30f, rect.width - 24f, 18f),
                "v1.9  •  github.com/ImDanOush/VehicleNoiseSynthesizer", mutedLabelStyle);
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
                : $"Active: clips {activeSrcLo}–{activeSrcHi}  ({Mathf.RoundToInt(bankProp.GetArrayElementAtIndex(activeSrcLo).FindPropertyRelative("rpmValue").intValue)}–{Mathf.RoundToInt(bankProp.GetArrayElementAtIndex(activeSrcHi).FindPropertyRelative("rpmValue").intValue)} RPM)";
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
        private const float ClipElementH = 62f;

        private ReorderableList BuildClipList(SerializedProperty prop, string label)
        {
            var list = new ReorderableList(serializedObject, prop, true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r, label),
                elementHeightCallback = i => ClipElementH,
                onAddCallback = l =>
                {
                    prop.arraySize++;
                    var el = prop.GetArrayElementAtIndex(prop.arraySize - 1);
                    el.FindPropertyRelative("audioClip").objectReferenceValue = null;
                    el.FindPropertyRelative("rpmValue").intValue = 1000;
                    el.FindPropertyRelative("volumeOffset").floatValue = 0f;
                    el.FindPropertyRelative("pitchOffset").floatValue = 0f;
                    el.FindPropertyRelative("rpmPitchTracking").floatValue = 1f;
                    el.FindPropertyRelative("minPitch").floatValue = 0.5f;
                    el.FindPropertyRelative("maxPitch").floatValue = 2.5f;
                    el.FindPropertyRelative("description").stringValue = "";
                    serializedObject.ApplyModifiedProperties();
                }
            };

            list.drawElementCallback = (rect, index, active, focused) =>
            {
                var el = prop.GetArrayElementAtIndex(index);
                var clipP = el.FindPropertyRelative("audioClip");
                var rpmP = el.FindPropertyRelative("rpmValue");
                var volP = el.FindPropertyRelative("volumeOffset");
                var pitP = el.FindPropertyRelative("pitchOffset");
                var minP = el.FindPropertyRelative("minPitch");
                var maxP = el.FindPropertyRelative("maxPitch");

                rect.y += 2f;
                float w = rect.width;
                float r1y = rect.y;
                float r2y = rect.y + ClipRowH + 2f;
                float fldH = EditorGUIUtility.singleLineHeight;

                EditorGUI.PropertyField(new Rect(rect.x, r1y, w * 0.42f, fldH), clipP, GUIContent.none);

                Rect rpmLabelR = new Rect(rect.x + w * 0.43f, r1y, 30f, fldH);
                Rect rpmFieldR = new Rect(rpmLabelR.xMax + 2f, r1y, 56f, fldH);
                GUI.Label(rpmLabelR, "RPM", EditorStyles.miniLabel);
                int newRpm = EditorGUI.IntField(rpmFieldR, rpmP.intValue);
                if (newRpm != rpmP.intValue) rpmP.intValue = Mathf.Max(0, newRpm);

                float pitchLabelW = 42f;
                GUI.Label(new Rect(rect.x, r2y, pitchLabelW, fldH), "Pitch", EditorStyles.miniLabel);

                float sliderAreaX = rect.x + pitchLabelW + 4f;
                float sliderWidth = (w - pitchLabelW - 20f) / 2f;
                Rect loR = new Rect(sliderAreaX, r2y, sliderWidth, fldH);
                Rect hiR = new Rect(loR.xMax + 6f, r2y, sliderWidth, fldH);

                Rect loLabelR = new Rect(loR.x, r2y, 18f, fldH);
                GUI.Label(loLabelR, "Lo", EditorStyles.miniLabel);
                Rect loSliderR = new Rect(loLabelR.xMax + 2f, r2y, loR.width - 58f, fldH);
                Rect loValR = new Rect(loSliderR.xMax + 2f, r2y, 36f, fldH);
                EditorGUI.Slider(loSliderR, minP, 0.01f, 4f, GUIContent.none);
                GUI.Label(loValR, minP.floatValue.ToString("F2"), EditorStyles.centeredGreyMiniLabel);

                Rect hiLabelR = new Rect(hiR.x, r2y, 18f, fldH);
                GUI.Label(hiLabelR, "Hi", EditorStyles.miniLabel);
                Rect hiSliderR = new Rect(hiLabelR.xMax + 2f, r2y, hiR.width - 58f, fldH);
                Rect hiValR = new Rect(hiSliderR.xMax + 2f, r2y, 36f, fldH);
                EditorGUI.Slider(hiSliderR, maxP, minP.floatValue, 4f, GUIContent.none);
                GUI.Label(hiValR, maxP.floatValue.ToString("F2"), EditorStyles.centeredGreyMiniLabel);

                Rect vizR = new Rect(rect.x, r2y + fldH + 3f, w, 5f);
                float normMin = Mathf.InverseLerp(0.01f, 4f, minP.floatValue);
                float normMax = Mathf.InverseLerp(0.01f, 4f, maxP.floatValue);
                float vizL = Mathf.Lerp(vizR.x, vizR.xMax, normMin);
                float vizR2 = Mathf.Lerp(vizR.x, vizR.xMax, normMax);
                EditorGUI.DrawRect(new Rect(vizL, vizR.y, vizR2 - vizL, vizR.height), new Color(0.23f, 0.76f, 1f, 0.6f));
                GUI.Label(new Rect(vizR.x, vizR.y + 5f, 20f, 12f), "0", EditorStyles.miniLabel);
                GUI.Label(new Rect(vizR.xMax - 12f, vizR.y + 5f, 16f, 12f), "4", EditorStyles.miniLabel);
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
            targetedShiftPitchProp = serializedObject.FindProperty("targetedShiftPitch");
            autoBlipProp = serializedObject.FindProperty("autoBlip");
            rpmDeviationProp = serializedObject.FindProperty("rpmdeviation");
            pitchCurveProp = serializedObject.FindProperty("pitchCurve");
            volumeCurveProp = serializedObject.FindProperty("volumeCurve");
            loadEffectivenessOnPitchProp = serializedObject.FindProperty("loadEffectivenessOnPitch");
            idlePitchProp = serializedObject.FindProperty("idlePitch");
            idleVolumeProp = serializedObject.FindProperty("idleVolume");
            keepBankClipsPlayingProp = serializedObject.FindProperty("keepBankClipsPlaying");
            clipVolumeResponseTimeProp = serializedObject.FindProperty("clipVolumeResponseTime");
            clipPitchResponseTimeProp = serializedObject.FindProperty("clipPitchResponseTime");
            rpmResponseTimeProp = serializedObject.FindProperty("rpmResponseTime");
            loadResponseTimeProp = serializedObject.FindProperty("loadResponseTime");
            pairHysteresisRpmProp = serializedObject.FindProperty("pairHysteresisRpm");
            pairHoldCyclesProp = serializedObject.FindProperty("pairHoldCycles");
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
            burbleLoadThresholdProp = serializedObject.FindProperty("burbleLoadThreshold");
            burbleRPMDropThresholdProp = serializedObject.FindProperty("burbleRPMDropThreshold");
            burbleProbabilityProp = serializedObject.FindProperty("burbleProbability");
            minBurbleDelayProp = serializedObject.FindProperty("minBurbleDelay");
            maxBurbleDelayProp = serializedObject.FindProperty("maxBurbleDelay");
            burbleRandomPitchVariationProp = serializedObject.FindProperty("burbleRandomPitchVariation");
            burbleFadeRateProp = serializedObject.FindProperty("burbleFadeRate");
            enableEngineLuggingProp = serializedObject.FindProperty("enableEngineLugging");
            luggingSoundsProp = serializedObject.FindProperty("luggingSounds");
            luggingVolumeProp = serializedObject.FindProperty("luggingVolume");
            luggingMinRPMThresholdProp = serializedObject.FindProperty("luggingMinRPMThreshold");
            luggingMaxRPMThresholdProp = serializedObject.FindProperty("luggingMaxRPMThreshold");
            luggingMinLoadThresholdProp = serializedObject.FindProperty("luggingMinLoadThreshold");
            luggingFadeInSpeedProp = serializedObject.FindProperty("luggingFadeInSpeed");
            luggingFadeOutSpeedProp = serializedObject.FindProperty("luggingFadeOutSpeed");
            luggingBasePitchProp = serializedObject.FindProperty("luggingBasePitch");
            luggingRandomPitchVariationProp = serializedObject.FindProperty("luggingRandomPitchVariation");
        }
    }
}
#endif
