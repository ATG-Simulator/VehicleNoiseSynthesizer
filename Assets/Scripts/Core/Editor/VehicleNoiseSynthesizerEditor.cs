#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AroundTheGroundSimulator
{
    /// <summary>
    /// Custom inspector for <see cref="VehicleNoiseSynthesizer"/>.
    /// Draws foldout sections, an accurate sqrt volume blend & RPM-pitch visualization,
    /// a load crossfade preview, and a combustion chart.
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

        // ── Visual constants ──────────────────────────────────────────────
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

        // ═══════════════════════════════════════════════════════════════════
        //  Top bar
        // ═══════════════════════════════════════════════════════════════════
        private void DrawTopBar()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 56f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BgColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(1f, 1f, 1f, 0.07f));
            EditorGUI.LabelField(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 22f), "Vehicle Noise Synthesizer", headerStyle);
            EditorGUI.LabelField(new Rect(rect.x + 12f, rect.y + 30f, rect.width - 24f, 18f),
                "Two-neighbour sqrt-blend editor  •  github.com/ImDanOush/VehicleNoiseSynthesizer", mutedLabelStyle);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Load crossfade preview
        // ═══════════════════════════════════════════════════════════════════
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

        // ═══════════════════════════════════════════════════════════════════
        //  Combustion preview
        // ═══════════════════════════════════════════════════════════════════
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
            float sampleRpm = Application.isPlaying && synth != null ? synth.rpm : (synth != null && synth.debug ? synth.debugrpm : maxRpm * 0.35f);
            float combustionHz = (sampleRpm / 60f) * eventsPerRev;
            float holdCycles = Mathf.Max(0f, pairHoldCyclesProp.floatValue);
            float holdTime = combustionHz > 0.001f ? holdCycles / combustionHz : 0f;
            GUI.Label(new Rect(rect.x + 12f, rect.y + 54f, rect.width - 24f, 16f),
                $"Combustion events/rev: {eventsPerRev:0.##}   ·   Current fire freq: {combustionHz:0.0} Hz   ·   Pair hold: {holdTime * 1000f:0.0} ms", mutedLabelStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 70f, rect.width - 24f, 16f),
                "Higher RPM raises firing frequency, so the pair-hold window naturally shortens.", mutedLabelStyle);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Blend visualization — volume (sqrt) + pitch (RPM-band)
        // ═══════════════════════════════════════════════════════════════════
        private void DrawBlendVisualization(SerializedProperty bankProp, bool isAcc)
        {
            int count = bankProp.arraySize;
            if (count == 0) { EditorGUILayout.HelpBox("No clips in this bank.  Add clips above to see the blend preview.", MessageType.Info); return; }

            float maxRpm = Mathf.Max(1000f, maximumTheoricalRPMProp.floatValue);
            Rect totalRect = GUILayoutUtility.GetRect(0f, TimelineHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(totalRect, BgColor);

            // Full-height volume visualization
            Rect volRect = new Rect(totalRect.x + 44f, totalRect.y + 6f, totalRect.width - 52f, totalRect.height - 22f);
            GUI.Label(new Rect(totalRect.x + 2f, volRect.y, 38f, 14f), "Volume", mutedLabelStyle);

            // Volume visualization — per-pair envelope (runtime: only 2 clips play at once)
            Color[] colors = GenerateColors(count, isAcc);

            const int envSamples = 200;
            float[] envelopeY = new float[envSamples];
            for (int s = 0; s < envSamples; s++) envelopeY[s] = 0f;

            List<Vector3[]> allClipCurves = new List<Vector3[]>();

            // Build envelope from ADJACENT PAIRS — exactly like runtime FindStableNeighbourPair
            for (int p = 0; p < count - 1; p++)
            {
                float loRef = bankProp.GetArrayElementAtIndex(p).FindPropertyRelative("rpmValue").intValue;
                float hiRef = bankProp.GetArrayElementAtIndex(p + 1).FindPropertyRelative("rpmValue").intValue;
                float loRefF = Mathf.Max(1f, loRef);
                float hiRefF = Mathf.Max(1f, hiRef);

                // In this pair, lo weight = cos(t*PI/2), hi weight = sin(t*PI/2) (constant-power pan)
                for (int s = 0; s < envSamples; s++)
                {
                    float sampleRpm = Mathf.Lerp(0f, maxRpm, s / (float)(envSamples - 1));
                    if (sampleRpm < loRefF || sampleRpm > hiRefF) continue;
                    float t = Mathf.InverseLerp(loRefF, hiRefF, sampleRpm);
                    float angle = t * Mathf.PI * 0.5f;
                    float loW = Mathf.Cos(angle);
                    float hiW = Mathf.Sin(angle);
                    float sum = loW + hiW;
                    if (sum > envelopeY[s]) envelopeY[s] = sum;
                }
            }

            // Build per-clip curves (each clip draws its rising half + falling half)
            for (int i = 0; i < count; i++)
            {
                float refRpm = Mathf.Max(1f, bankProp.GetArrayElementAtIndex(i).FindPropertyRelative("rpmValue").intValue);
                float prevRpm = i > 0 ? bankProp.GetArrayElementAtIndex(i - 1).FindPropertyRelative("rpmValue").intValue : 0f;
                float nextRpm = i < count - 1 ? bankProp.GetArrayElementAtIndex(i + 1).FindPropertyRelative("rpmValue").intValue : maxRpm;

                // Clip i as HI in pair (i-1, i): rising sqrt(t) from prevRpm to refRpm
                // Clip i as LO in pair (i, i+1): falling sqrt(1-t) from refRpm to nextRpm
                Vector3[] curve = BuildCosCurve(volRect, prevRpm, refRpm, nextRpm, maxRpm);
                allClipCurves.Add(curve);
            }

            // Draw envelope fill (single smooth area)
            Handles.BeginGUI();
            Color envFill = isAcc ? new Color(0.23f, 0.76f, 1f, 0.12f) : new Color(1f, 0.57f, 0.22f, 0.12f);
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

            // Draw per-clip lines on top
            for (int i = 0; i < allClipCurves.Count; i++)
            {
                Handles.color = new Color(colors[i].r, colors[i].g, colors[i].b, 0.85f);
                Handles.DrawAAPolyLine(2f, allClipCurves[i]);
            }
            Handles.EndGUI();

            // RPM scale
            DrawRpmScale(totalRect, maxRpm);

            // Current RPM marker
            float rpm = Application.isPlaying && synth ? synth.rpm : (synth && synth.debug ? synth.debugrpm : maxRpm * 0.35f);
            float mx = Mathf.Lerp(totalRect.x + 44f, totalRect.xMax - 4f, Mathf.Clamp01(rpm / maxRpm));
            EditorGUI.DrawRect(new Rect(mx, totalRect.y, 1.5f, totalRect.height), new Color(1f, 1f, 1f, 0.5f));
            GUI.Label(new Rect(mx - 30f, totalRect.y, 60f, 14f), Mathf.RoundToInt(rpm).ToString(), new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
        }

        // Builds a single clip's cos/sin curve as screen-space points [rising sin, falling cos]
        private Vector3[] BuildCosCurve(Rect rect, float leftEdge, float center, float rightEdge, float maxRpm)
        {
            int n = 40;
            Vector3[] pts = new Vector3[n * 2 - 1];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float rpm = Mathf.Lerp(leftEdge, center, t);
                float angle = t * Mathf.PI * 0.5f;
                float w = Mathf.Sin(angle);  // rising: sin(t*PI/2)
                pts[i] = new Vector3(RpmToX(rpm, rect, maxRpm), WeightToY(w, rect), 0f);
            }
            for (int i = 1; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float rpm = Mathf.Lerp(center, rightEdge, t);
                float angle = t * Mathf.PI * 0.5f;
                float w = Mathf.Cos(angle);  // falling: cos(t*PI/2)
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

        // ═══════════════════════════════════════════════════════════════════
        //  Draw utilities
        // ═══════════════════════════════════════════════════════════════════
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

        private string Shorten(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 1) + "…";

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

        // ═══════════════════════════════════════════════════════════════════
        //  ReorderableList with labelled per-clip fields (2 rows per clip)
        // ═══════════════════════════════════════════════════════════════════
        private const float ClipRowH = 20f;
        private const float ClipElementH = ClipRowH * 2f + 6f;

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

                // Row 1: AudioClip | RPM | Vol | PitchOff
                EditorGUI.PropertyField(new Rect(rect.x, r1y, w * 0.42f, fldH), clipP, GUIContent.none);
                float x = rect.x + w * 0.43f;
                DrawLabeledField("RPM", ref x, w, r1y, 28f, rpmP, 52f);
                // DrawLabeledField("Vol", ref x, w, r1y, 22f, volP, 46f);
                // DrawLabeledField("POff", ref x, w, r1y, 28f, pitP, 46f);

                // Row 2: min/max pitch sliders + range viz line
                Rect minR = new Rect(rect.x + 48f, r2y, (w - 96f) / 2f, fldH);
                Rect maxR = new Rect(minR.xMax + 8f, r2y, (w - 96f) / 2f, fldH);
                GUI.Label(new Rect(rect.x, r2y, 46f, fldH), "min", EditorStyles.miniLabel);
                EditorGUI.Slider(minR, minP, 0.01f, 4f, GUIContent.none);
                EditorGUI.Slider(maxR, maxP, minP.floatValue, 4f, GUIContent.none);
                Rect vizR = new Rect(rect.x, r2y + fldH + 2f, w, 4f);
                float normMin = Mathf.InverseLerp(0.01f, 4f, minP.floatValue);
                float normMax = Mathf.InverseLerp(0.01f, 4f, maxP.floatValue);
                float vizL = Mathf.Lerp(vizR.x, vizR.xMax, normMin);
                float vizR2 = Mathf.Lerp(vizR.x, vizR.xMax, normMax);
                EditorGUI.DrawRect(new Rect(vizL, vizR.y, vizR2 - vizL, vizR.height), new Color(0.23f, 0.76f, 1f, 0.5f));
            };

            return list;
        }

        private static void DrawLabeledField(string label, ref float x, float totalW, float y, float labelW, SerializedProperty prop, float fieldW)
        {
            Rect lr = new Rect(x, y, labelW, EditorGUIUtility.singleLineHeight);
            GUI.Label(lr, label, EditorStyles.miniLabel);
            Rect fr = new Rect(x + labelW, y, fieldW, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(fr, prop, GUIContent.none);
            x += labelW + fieldW + 4f;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Styles & cache
        // ═══════════════════════════════════════════════════════════════════
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
