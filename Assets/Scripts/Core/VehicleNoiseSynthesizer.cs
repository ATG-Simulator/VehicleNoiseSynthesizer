using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;
#if !UNITY_WEBGL
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
#endif

namespace AroundTheGroundSimulator
{
    [AddComponentMenu("ATG Audio/Vehicle Noise Synthesizer")]
    [HelpURL("https://github.com/ImDanOush/VehicleNoiseSynthesizer")]
    public class VehicleNoiseSynthesizer : MonoBehaviour
    {
        public enum MixerType { Intake, Engine, Exhaust, Transmission, Differential }

        public enum CombustionCycleMode { FourStroke, TwoStroke }

        [Serializable]
        public class EngineAudioClipData
        {
            [Tooltip("Audio clip containing engine sound.")]
            public AudioClip audioClip;

            [Tooltip("RPM value at which this clip was recorded.")]
            [Range(0, 10000)]
            public int rpmValue = 1000;

            [Tooltip("How strongly this clip follows RPM-ratio pitch tracking.")]
            [Range(0f, 1f)]
            public float rpmPitchTracking = 1f;

            [Tooltip("Minimum allowed playback pitch for this clip.")]
            [Range(0.01f, 10f)]
            public float minPitch = 0.5f;

            [Tooltip("Maximum allowed playback pitch for this clip.")]
            [Range(0.01f, 10f)]
            public float maxPitch = 2.5f;

            [Tooltip("Optional description for this audio clip.")]
            public string description;

            [Tooltip("Per-clip volume trim.")]
            [Range(-1.0f, 1.0f)]
            public float volumeOffset = 0f;

            [Tooltip("Per-clip pitch trim added to the RPM-tracked ratio.")]
            [Range(-0.5f, 0.5f)]
            public float pitchOffset = 0f;
        }

        private sealed class RuntimeLayer
        {
            public GameObject host;
            public AudioSource source;
            public AudioLowPassFilter lowPass;
            public AudioHighPassFilter highPass;
            public AudioDistortionFilter distortion;
            public AudioChorusFilter chorus;
            public AudioReverbFilter reverb;
            public AudioClip clip;
            public float referenceRpm;
            public float rpmPitchTracking;
            public float minPitch;
            public float maxPitch;
            public float volumeOffset;
            public float pitchOffset;
        }

        private struct BankBlendState
        {
            public bool initialized;
            public int lowIndex;
            public int highIndex;
            public float holdUntilTime;
        }

        private const int CurveBakeResolution = 256;
        private float[] bakedPitchCurve;
        private float[] bakedVolumeCurve;
        private float[] bakedDistortionCurve;
        private float[] bakedLowPassCurve;

#if !UNITY_WEBGL
        private VehicleCalcOutput _lastJobOutput;
#endif
        private static float[] BakeCurve(AnimationCurve curve)
        {
            float[] table = new float[CurveBakeResolution];
            float step = 1f / (CurveBakeResolution - 1);
            for (int i = 0; i < CurveBakeResolution; i++)
                table[i] = curve.Evaluate(i * step);
            return table;
        }

        private static float SampleBakedCurve(float[] table, float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            float scaled = t * (CurveBakeResolution - 1);
            int lo = (int)scaled;
            int hi = lo + 1;
            if (hi >= CurveBakeResolution) return table[CurveBakeResolution - 1];
            return table[lo] + (table[hi] - table[lo]) * (scaled - lo);
        }

        private void BakeAllCurves()
        {
            bakedPitchCurve = BakeCurve(pitchCurve);
            bakedVolumeCurve = BakeCurve(volumeCurve);
            bakedDistortionCurve = BakeCurve(distortionCurve);
            bakedLowPassCurve = BakeCurve(lowPassCurve);
        }

#if !UNITY_WEBGL
        // ─────────────────────────────────────────────────────────────────
        //  Blittable input snapshot — written each frame on the main thread,
        //  read-only inside the Burst job.
        // ─────────────────────────────────────────────────────────────────
        private struct VehicleCalcInput
        {
            public float smoothedRpm;
            public float smoothedLoad;
            public float previousSmoothedRpm;
            public float previousSmoothedLoad;
            public float clampedRpm;
            public float normalizedRpm;
            public float idleRpm;
            public float maxRpm;
            public float idlePitch;
            public float loadEffectivenessOnPitch;
            public float targetedShiftPitch;
            public float shiftPitchOsc;
            public float loadCrossoverPoint;
            public float loadBlendWidth;
            public bool autoBlip;
            public bool launchMode;
            public bool nonDecelerateAudiosMode;
            public float idleVolume;
            public float masterVolume;
            public float loadVolumeAccChangerFactor;
            public float loadVolumeDccChangerFactor;
            public float loadVolumeChangerMinValue;
            public float maxVolumeAcc;
            public float maxVolumeDcc;
            public float acPitchTrim;
            public float dcPitchTrim;
            public int accCount;
            public int decCount;
            public float combustionEventsPerRev;
            public float lowPassIntensity;
            public float lowPassStrength;
            public float mufflingIntensity;
            public float highPassStrength;
            public float resonanceStrength;
            public float distortionIntensity;
            public float distortionStrength;
            public float chorusStrength;
            public float reverbStrength;

            // ── Hysteresis state carried in from previous frame ──────────
            public bool accStateInitialized;
            public int accStateLowIndex;
            public int accStateHighIndex;
            public float accStateHoldUntilTime;

            public bool decStateInitialized;
            public int decStateLowIndex;
            public int decStateHighIndex;
            public float decStateHoldUntilTime;

            public float pairHysteresisRpm;
            public float pairHoldCycles;
            public float currentTime;            // Time.time snapshot
        }

        // ─────────────────────────────────────────────────────────────────
        //  Blittable output — written by the job, applied on the main thread.
        // ─────────────────────────────────────────────────────────────────
        private struct VehicleCalcOutput
        {
            public float finalPitch;
            public float finalAccVol;
            public float finalDecVol;
            public float lowPassTarget;
            public float lowPassQTarget;
            public float highPassTarget;
            public float highPassQTarget;
            public float distortionTarget;
            public float chorusWet;
            public float chorusDepth;
            public float chorusRate;
            public float chorusDryTarget;
            public float reverbLevel;
            public float reverbDecay;
            public float reverbAmountDb;

            // ── Hysteresis state written back from the job ───────────────
            public bool accStateInitialized;
            public int accStateLowIndex;
            public int accStateHighIndex;
            public float accStateHoldUntilTime;

            public bool decStateInitialized;
            public int decStateLowIndex;
            public int decStateHighIndex;
            public float decStateHoldUntilTime;

            public int accLowIndex;
            public int accHighIndex;
            public float accLowVolume;
            public float accHighVolume;
            public float accLowPitch;
            public float accHighPitch;
            public int decLowIndex;
            public int decHighIndex;
            public float decLowVolume;
            public float decHighVolume;
            public float decLowPitch;
            public float decHighPitch;
        }

        private struct LayerData
        {
            public float referenceRpm;
            public float rpmPitchTracking;
            public float minPitch;
            public float maxPitch;
            public float volumeOffset;
            public float pitchOffset;
        }

        [BurstCompile]
        private struct CalculateBatchJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<VehicleCalcInput> Inputs;
            [ReadOnly] public NativeArray<LayerData> AccLayerData;
            [ReadOnly] public NativeArray<LayerData> DecLayerData;
            [ReadOnly] public NativeArray<int> AccLayerOffsets;
            [ReadOnly] public NativeArray<int> DecLayerOffsets;
            [ReadOnly] public NativeArray<float> BakedPitchCurves;
            [ReadOnly] public NativeArray<float> BakedVolumeCurves;
            [ReadOnly] public NativeArray<float> BakedDistortionCurves;
            [ReadOnly] public NativeArray<float> BakedLowPassCurves;
            public int CurveSamples;
            public NativeArray<VehicleCalcOutput> Outputs;

            public void Execute(int vehicleIndex)
            {
                VehicleCalcInput inp = Inputs[vehicleIndex];
                VehicleCalcOutput o = default;
                int curveBase = vehicleIndex * CurveSamples;

                // ── Global pitch ────────────────────────────────────────
                // [fix] Smooth idlePitch blend like idleVolume: Lerp(idlePitch, curve, normalizedRpm)
                float curvePitch = SampleJob(BakedPitchCurves, curveBase, inp.normalizedRpm);
                float pitchShape = Lerp(inp.idlePitch, curvePitch, inp.normalizedRpm);
                float loadPitchContrib = inp.smoothedLoad * inp.loadEffectivenessOnPitch;
                o.finalPitch = MathMax(0.01f,
                    pitchShape + loadPitchContrib + inp.targetedShiftPitch + inp.shiftPitchOsc);

                // ── Acc/Dec blend ────────────────────────────────────────
                float halfWidth = MathMax(0.005f, inp.loadBlendWidth * 0.5f);
                float start = Clamp01(inp.loadCrossoverPoint - halfWidth);
                float end = Clamp01(inp.loadCrossoverPoint + halfWidth);
                float accBlend = SmoothStep(InverseLerp(start, end, inp.smoothedLoad));

                if (inp.autoBlip)
                {
                    bool rpmJumpUp = inp.previousSmoothedRpm + 75f < inp.clampedRpm;
                    bool snappedIdle = inp.clampedRpm <= MathMax(1f, inp.idleRpm) &&
                                      inp.previousSmoothedRpm > inp.clampedRpm + 75f;
                    if (rpmJumpUp || snappedIdle) accBlend = 1f;
                }

                float decBlend = 1f - accBlend;
                float rpmVolume = SampleJob(BakedVolumeCurves, curveBase, inp.normalizedRpm);
                float idleBias = Lerp(inp.idleVolume, 1f, inp.normalizedRpm);
                o.finalAccVol = Clamp01(accBlend * rpmVolume * idleBias);
                o.finalDecVol = Clamp01(decBlend * rpmVolume * idleBias);

                if (inp.launchMode) { o.finalAccVol = 1f; o.finalDecVol = 0f; }
                else if (inp.nonDecelerateAudiosMode)
                {
                    o.finalAccVol = Clamp01(MathMax(o.finalAccVol, o.finalDecVol));
                    o.finalDecVol = 0f;
                }
                if (inp.clampedRpm <= MathMax(1f, inp.idleRpm))
                    o.finalAccVol = MathMax(o.finalAccVol, inp.idleVolume);

                // ── Bank evaluation with hysteresis ──────────────────────
                EvaluateBankInJob(inp, o.finalPitch, o.finalAccVol,
                    AccLayerData, AccLayerOffsets[vehicleIndex], inp.accCount,
                    true, inp.maxVolumeAcc, inp.acPitchTrim,
                    inp.accStateInitialized, inp.accStateLowIndex,
                    inp.accStateHighIndex, inp.accStateHoldUntilTime,
                    out o.accLowIndex, out o.accHighIndex,
                    out o.accLowVolume, out o.accHighVolume,
                    out o.accLowPitch, out o.accHighPitch,
                    out o.accStateInitialized, out o.accStateLowIndex,
                    out o.accStateHighIndex, out o.accStateHoldUntilTime);

                EvaluateBankInJob(inp, o.finalPitch, o.finalDecVol,
                    DecLayerData, DecLayerOffsets[vehicleIndex], inp.decCount,
                    false, inp.maxVolumeDcc, inp.dcPitchTrim,
                    inp.decStateInitialized, inp.decStateLowIndex,
                    inp.decStateHighIndex, inp.decStateHoldUntilTime,
                    out o.decLowIndex, out o.decHighIndex,
                    out o.decLowVolume, out o.decHighVolume,
                    out o.decLowPitch, out o.decHighPitch,
                    out o.decStateInitialized, out o.decStateLowIndex,
                    out o.decStateHighIndex, out o.decStateHoldUntilTime);

                // ── Filters ──────────────────────────────────────────────
                float lpCurveValue = ClampF(
                    SampleJob(BakedLowPassCurves, curveBase, inp.smoothedLoad), 500f, 22000f);
                float lpMix = Clamp01(MathMax(inp.lowPassIntensity,
                    inp.lowPassStrength + inp.mufflingIntensity));
                o.lowPassTarget = Lerp(22000f, lpCurveValue, lpMix);

                float hpAmount = Clamp01(inp.normalizedRpm * inp.normalizedRpm * inp.highPassStrength);
                o.highPassTarget = Lerp(10f, 1800f, hpAmount);

                float resShape = MathSin2(inp.normalizedRpm * 3.14159265f);
                o.lowPassQTarget = Lerp(1f, 8f, Clamp01(resShape * inp.resonanceStrength));
                o.highPassQTarget = Lerp(1f, 2.2f, hpAmount);

                float distDrive = SampleJob(BakedDistortionCurves, curveBase, inp.normalizedRpm) *
                                   (inp.smoothedLoad + 0.5f);
                o.distortionTarget = Clamp01(
                    distDrive * inp.distortionIntensity * (1f + inp.distortionStrength));

                float chorusAmount = Clamp01(
                    InverseLerp(0.3f, 1f, inp.normalizedRpm) * inp.chorusStrength);
                o.chorusWet = Lerp(0f, 0.55f, chorusAmount);
                o.chorusDepth = Lerp(0f, 0.7f, chorusAmount);
                o.chorusRate = Lerp(0.8f, 2.1f, chorusAmount);
                float totalWet = o.chorusWet * 0.6f + o.chorusWet * 0.3f + o.chorusWet * 0.1f;
                o.chorusDryTarget = MathMax(0f, 1f - totalWet);

                float reverbAmount = Clamp01(
                    MathMax(inp.normalizedRpm, inp.smoothedLoad) * inp.reverbStrength);
                o.reverbLevel = Lerp(-10000f, -1000f, reverbAmount);
                o.reverbDecay = Lerp(0.8f, 2.3f, reverbAmount);
                o.reverbAmountDb = Lerp(-80f, 0f, reverbAmount);

                Outputs[vehicleIndex] = o;
            }

            // ── Pure-math helpers (no Unity API, Burst-safe) ─────────────

            private static float SampleJob(NativeArray<float> table, int baseIndex, float t)
            {
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                const int n = 256;
                float scaled = t * (n - 1);
                int lo = (int)scaled;
                int hi = lo + 1;
                if (hi >= n) return table[baseIndex + n - 1];
                return table[baseIndex + lo] +
                       (table[baseIndex + hi] - table[baseIndex + lo]) * (scaled - lo);
            }

            private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
            private static float ClampF(float v, float lo, float hi) =>
                v < lo ? lo : v > hi ? hi : v;
            private static float MathMax(float a, float b) => a > b ? a : b;
            private static float Lerp(float a, float b, float t) => a + (b - a) * t;
            // [fix] Clamp to [0,1] to match Unity's Mathf.InverseLerp — prevents negative
            // crossfade weights when the hysteresis-selected pair does not bracket the current RPM.
            private static float InverseLerp(float a, float b, float t) =>
                (b - a) < 0.00001f ? 0f : Clamp01((t - a) / (b - a));
            private static float SmoothStep(float t)
            {
                t = Clamp01(t);
                return t * t * (3f - 2f * t);
            }

            // ── EvaluateBankInJob — with RPM hysteresis + hold timer ─────
            private static void EvaluateBankInJob(
                VehicleCalcInput inp, float finalPitch, float bankFinalVol,
                NativeArray<LayerData> layerData, int layerOffset, int count,
                bool isAcc, float bankVolumeLimit, float bankPitchTrim,
                // hysteresis state IN
                bool stateInitialized,
                int stateLowIndex,
                int stateHighIndex,
                float stateHoldUntilTime,
                // audio outputs
                out int outLowIndex, out int outHighIndex,
                out float outLowVolume, out float outHighVolume,
                out float outLowPitch, out float outHighPitch,
                // hysteresis state OUT
                out bool outStateInitialized,
                out int outStateLow,
                out int outStateHigh,
                out float outStateHoldUntil)
            {
                outLowIndex = 0; outHighIndex = 0;
                outLowVolume = 0f; outHighVolume = 0f;
                outLowPitch = 1f; outHighPitch = 1f;

                // Pass hysteresis state through unchanged until we decide otherwise
                outStateInitialized = stateInitialized;
                outStateLow = stateLowIndex;
                outStateHigh = stateHighIndex;
                outStateHoldUntil = stateHoldUntilTime;

                if (count <= 0) return;

                // ── Load gain ────────────────────────────────────────────
                float loadGainRaw = isAcc
                    ? Lerp(inp.loadVolumeChangerMinValue, 1f, inp.smoothedLoad)
                    : Lerp(inp.loadVolumeChangerMinValue, 1f, 1f - inp.smoothedLoad);
                float loadGain = isAcc
                    ? Lerp(1f, loadGainRaw, inp.loadVolumeAccChangerFactor)
                    : Lerp(1f, loadGainRaw, inp.loadVolumeDccChangerFactor);
                float bankBaseGain = inp.masterVolume * bankFinalVol * MathMax(0f, loadGain);

                // ── Find ideal pair for this RPM ─────────────────────────
                int idealLo = 0, idealHi = 0;
                if (count > 1)
                {
                    bool found = false;
                    for (int i = 0; i < count - 1; i++)
                    {
                        if (inp.clampedRpm >= layerData[layerOffset + i].referenceRpm &&
                            inp.clampedRpm <= layerData[layerOffset + i + 1].referenceRpm)
                        {
                            idealLo = i; idealHi = i + 1; found = true; break;
                        }
                    }
                    if (!found)
                    {
                        if (inp.clampedRpm < layerData[layerOffset].referenceRpm)
                            idealLo = idealHi = 0;
                        else
                            idealLo = idealHi = count - 1;
                    }
                }

                // ── Hysteresis arbitration ───────────────────────────────
                int lo, hi;
                if (!stateInitialized)
                {
                    lo = idealLo; hi = idealHi;
                    outStateInitialized = true;
                    outStateLow = lo;
                    outStateHigh = hi;
                    float holdDur = (inp.combustionEventsPerRev > 0f && inp.smoothedRpm > 0f)
                        ? inp.pairHoldCycles / (inp.smoothedRpm / 60f * inp.combustionEventsPerRev)
                        : 0.05f;
                    outStateHoldUntil = inp.currentTime + holdDur;
                }
                else if (inp.currentTime < stateHoldUntilTime)
                {
                    lo = stateLowIndex < count ? stateLowIndex : count - 1;
                    hi = stateHighIndex < count ? stateHighIndex : count - 1;
                }
                else
                {
                    bool wantsSwitch = (idealLo != stateLowIndex || idealHi != stateHighIndex);
                    bool passesHysteresis = false;
                    if (wantsSwitch && count > 1)
                    {
                        // [fix] Use the transition boundary (current pair's edge), not the ideal pair's reference.
                        // Moving UP (e.g. 0,1→1,2): boundary = current high clip's refRpm (the clip being crossed).
                        // Moving DOWN (e.g. 1,2→0,1): boundary = current low clip's refRpm.
                        if (idealHi > stateHighIndex)
                        {
                            float boundary = layerData[layerOffset + stateHighIndex].referenceRpm;
                            passesHysteresis = inp.clampedRpm > boundary + inp.pairHysteresisRpm;
                        }
                        else if (idealHi < stateHighIndex)
                        {
                            float boundary = layerData[layerOffset + stateLowIndex].referenceRpm;
                            passesHysteresis = inp.clampedRpm < boundary - inp.pairHysteresisRpm;
                        }
                        else
                        {
                            passesHysteresis = true; // same hi, different lo
                        }
                    }

                    if (wantsSwitch && passesHysteresis)
                    {
                        // ── CRITICAL: step one pair at a time, not a multi-step jump ──
                        // Moving up: new pair is (prevHigh, prevHigh+1) — preserves continuity
                        // Moving down: new pair is (prevLow-1, prevLow)
                        if (idealHi > stateHighIndex)
                        {
                            lo = stateHighIndex < count ? stateHighIndex : count - 1;
                            hi = lo + 1 < count ? lo + 1 : lo;
                        }
                        else if (idealLo < stateLowIndex)
                        {
                            hi = stateLowIndex > 0 ? stateLowIndex : 0;
                            lo = hi - 1 >= 0 ? hi - 1 : 0;
                        }
                        else
                        {
                            lo = idealLo; hi = idealHi;
                        }

                        outStateLow = lo;
                        outStateHigh = hi;
                        float holdDur = (inp.combustionEventsPerRev > 0f && inp.smoothedRpm > 0f)
                            ? inp.pairHoldCycles / (inp.smoothedRpm / 60f * inp.combustionEventsPerRev)
                            : 0.05f;
                        outStateHoldUntil = inp.currentTime + holdDur;
                    }
                    else
                    {
                        lo = stateLowIndex < count ? stateLowIndex : count - 1;
                        hi = stateHighIndex < count ? stateHighIndex : count - 1;
                    }
                }

                outLowIndex = lo; outHighIndex = hi;

                // ── Crossfade blend ──────────────────────────────────────
                float t = 0f;
                if (lo != hi)
                {
                    float a = layerData[layerOffset + lo].referenceRpm;
                    float b = layerData[layerOffset + hi].referenceRpm;
                    t = InverseLerp(a, b, inp.clampedRpm);
                }

                if (lo == hi)
                {
                    LayerData ld = layerData[layerOffset + lo];
                    float g = bankBaseGain * MathMax(0f, 1f + ld.volumeOffset);
                    if (bankVolumeLimit > 0f) g = g > bankVolumeLimit ? bankVolumeLimit : g;
                    outLowVolume = outHighVolume = g;
                    outLowPitch = outHighPitch =
                        EvalPitchInJob(ld, inp.clampedRpm, finalPitch,
                                       bankPitchTrim, inp.combustionEventsPerRev, inp.maxRpm);
                    return;
                }

                // Constant-power crossfade: cos² + sin² = 1 (standard pan law)
                // Use math.sin/cos (not Bhaskara approx) for correct weights at extremes.
                float angle = t * 1.5707963f;
                float lowW = math.cos(angle);
                float highW = math.sin(angle);

                LayerData ldLo = layerData[layerOffset + lo];
                LayerData ldHi = layerData[layerOffset + hi];
                float gLo = bankBaseGain * lowW * MathMax(0f, 1f + ldLo.volumeOffset);
                float gHi = bankBaseGain * highW * MathMax(0f, 1f + ldHi.volumeOffset);

                if (bankVolumeLimit > 0f)
                {
                    gLo = gLo > bankVolumeLimit ? bankVolumeLimit : gLo;
                    gHi = gHi > bankVolumeLimit ? bankVolumeLimit : gHi;
                }

                outLowVolume = gLo;
                outHighVolume = gHi;
                outLowPitch = EvalPitchInJob(ldLo, inp.clampedRpm, finalPitch,
                                              bankPitchTrim, inp.combustionEventsPerRev, inp.maxRpm);
                outHighPitch = EvalPitchInJob(ldHi, inp.clampedRpm, finalPitch,
                                              bankPitchTrim, inp.combustionEventsPerRev, inp.maxRpm);
            }

            // [fix] Pitch = RPM-based progress mapped to [minPitch, maxPitch].
            // progress = clampedRpm/maxRpm — continuous across pair boundaries.
            private static float EvalPitchInJob(
                LayerData ld, float clampedRpm, float finalPitch,
                float bankPitchTrim, float combustionEventsPerRev, float maxRpm)
            {
                float progress = Clamp01(clampedRpm / MathMax(1f, maxRpm));
                float pitch = Lerp(ld.minPitch, ld.maxPitch, progress);
                pitch += (finalPitch - 1f) + bankPitchTrim + ld.pitchOffset;
                return ClampF(pitch, 0.01f, 10f);
            }

            // Fast Bhaskara sine approximation — no libm dependency, Burst-safe
            private static float MathSin2(float x)
            {
                const float pi = 3.14159265f;
                x = x % (2f * pi);
                if (x < 0f) x += 2f * pi;
                bool inv = x > pi;
                if (inv) x -= pi;
                float s = 4f * x * (pi - x) / (5f * pi * pi - 4f * x * (pi - x));
                return inv ? -s : s;
            }
            private static float MathCos(float x) => MathSin2(x + 1.5707963f);
            private static float MathSin(float x) => MathSin2(x);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  VehicleNoiseBatchManager — owns NativeArrays, drives the job each
        //  frame, distributes results back to each synthesizer instance.
        // ═══════════════════════════════════════════════════════════════════
        internal static partial class VehicleNoiseBatchManager
        {
            private static readonly List<VehicleNoiseSynthesizer> sInstances =
                new List<VehicleNoiseSynthesizer>();

            private static NativeArray<VehicleCalcInput> sInputs;
            private static NativeArray<VehicleCalcOutput> sOutputs;
            private static NativeArray<LayerData> sAccLayers;
            private static NativeArray<LayerData> sDecLayers;
            private static NativeArray<int> sAccOffsets;
            private static NativeArray<int> sDecOffsets;
            private static NativeArray<float> sBakedPitch;
            private static NativeArray<float> sBakedVolume;
            private static NativeArray<float> sBakedDistortion;
            private static NativeArray<float> sBakedLowPass;
            private static int sAllocatedCount = 0;
            private static bool sNeedsRebuild = false;
            private static float sLastDispatchFixedTime = -1f;

            public static void Register(VehicleNoiseSynthesizer instance)
            {
                if (!sInstances.Contains(instance))
                { sInstances.Add(instance); sNeedsRebuild = true; }
            }

            public static void Unregister(VehicleNoiseSynthesizer instance)
            {
                if (sInstances.Remove(instance)) sNeedsRebuild = true;
            }

            public static void ExecuteBatchIfNeeded(float deltaTime)
            {
                if (!ShouldDispatchThisFixedTick()) return;
                int count = sInstances.Count;
                if (count == 0) return;

                if (sNeedsRebuild || count != sAllocatedCount)
                    RebuildNativeArrays(count);

                // ── Pack inputs ──────────────────────────────────────────
                for (int v = 0; v < count; v++)
                {
                    VehicleNoiseSynthesizer syn = sInstances[v];
                    sInputs[v] = syn.PackJobInput();

                    int curveBase = v * CurveBakeResolution;
                    for (int i = 0; i < CurveBakeResolution; i++)
                    {
                        sBakedPitch[curveBase + i] = syn.bakedPitchCurve[i];
                        sBakedVolume[curveBase + i] = syn.bakedVolumeCurve[i];
                        sBakedDistortion[curveBase + i] = syn.bakedDistortionCurve[i];
                        sBakedLowPass[curveBase + i] = syn.bakedLowPassCurve[i];
                    }

                    int accOff = sAccOffsets[v];
                    for (int i = 0; i < syn.accLayers.Count; i++)
                    {
                        RuntimeLayer rl = syn.accLayers[i];
                        sAccLayers[accOff + i] = new LayerData
                        {
                            referenceRpm = rl.referenceRpm,
                            rpmPitchTracking = rl.rpmPitchTracking,
                            minPitch = rl.minPitch,
                            maxPitch = rl.maxPitch,
                            volumeOffset = rl.volumeOffset,
                            pitchOffset = rl.pitchOffset
                        };
                    }

                    int decOff = sDecOffsets[v];
                    for (int i = 0; i < syn.decLayers.Count; i++)
                    {
                        RuntimeLayer rl = syn.decLayers[i];
                        sDecLayers[decOff + i] = new LayerData
                        {
                            referenceRpm = rl.referenceRpm,
                            rpmPitchTracking = rl.rpmPitchTracking,
                            minPitch = rl.minPitch,
                            maxPitch = rl.maxPitch,
                            volumeOffset = rl.volumeOffset,
                            pitchOffset = rl.pitchOffset
                        };
                    }
                }

                // ── Schedule and immediately complete (one-frame pipeline) ─
                var job = new CalculateBatchJob
                {
                    Inputs = sInputs,
                    Outputs = sOutputs,
                    AccLayerData = sAccLayers,
                    DecLayerData = sDecLayers,
                    AccLayerOffsets = sAccOffsets,
                    DecLayerOffsets = sDecOffsets,
                    BakedPitchCurves = sBakedPitch,
                    BakedVolumeCurves = sBakedVolume,
                    BakedDistortionCurves = sBakedDistortion,
                    BakedLowPassCurves = sBakedLowPass,
                    CurveSamples = CurveBakeResolution
                };

                JobHandle handle = job.Schedule(count, 1);
                handle.Complete();

                // ── Distribute results ───────────────────────────────────
                for (int v = 0; v < count; v++)
                    sInstances[v].ApplyJobData(sOutputs[v]);

                // NOTE: ApplySlewAndEffects is called per-instance in CalculateAsync, always
            }

            private static void RebuildNativeArrays(int count)
            {
                DisposeNativeArrays();

                int totalAcc = 0, totalDec = 0;
                int[] accOffsets = new int[count];
                int[] decOffsets = new int[count];
                for (int v = 0; v < count; v++)
                {
                    accOffsets[v] = totalAcc;
                    decOffsets[v] = totalDec;
                    totalAcc += sInstances[v].accLayers.Count;
                    totalDec += sInstances[v].decLayers.Count;
                }

                int curves = count * CurveBakeResolution;
                sInputs = new NativeArray<VehicleCalcInput>(count, Allocator.Persistent);
                sOutputs = new NativeArray<VehicleCalcOutput>(count, Allocator.Persistent);
                sAccLayers = new NativeArray<LayerData>(Mathf.Max(1, totalAcc), Allocator.Persistent);
                sDecLayers = new NativeArray<LayerData>(Mathf.Max(1, totalDec), Allocator.Persistent);
                sAccOffsets = new NativeArray<int>(count, Allocator.Persistent);
                sDecOffsets = new NativeArray<int>(count, Allocator.Persistent);
                sBakedPitch = new NativeArray<float>(curves, Allocator.Persistent);
                sBakedVolume = new NativeArray<float>(curves, Allocator.Persistent);
                sBakedDistortion = new NativeArray<float>(curves, Allocator.Persistent);
                sBakedLowPass = new NativeArray<float>(curves, Allocator.Persistent);

                for (int v = 0; v < count; v++)
                {
                    sAccOffsets[v] = accOffsets[v];
                    sDecOffsets[v] = decOffsets[v];
                }

                sAllocatedCount = count;
                sNeedsRebuild = false;
            }

            private static void DisposeNativeArrays()
            {
                if (sInputs.IsCreated) sInputs.Dispose();
                if (sOutputs.IsCreated) sOutputs.Dispose();
                if (sAccLayers.IsCreated) sAccLayers.Dispose();
                if (sDecLayers.IsCreated) sDecLayers.Dispose();
                if (sAccOffsets.IsCreated) sAccOffsets.Dispose();
                if (sDecOffsets.IsCreated) sDecOffsets.Dispose();
                if (sBakedPitch.IsCreated) sBakedPitch.Dispose();
                if (sBakedVolume.IsCreated) sBakedVolume.Dispose();
                if (sBakedDistortion.IsCreated) sBakedDistortion.Dispose();
                if (sBakedLowPass.IsCreated) sBakedLowPass.Dispose();
                sAllocatedCount = 0;
            }

            public static void DisposeAll() => DisposeNativeArrays();

            public static bool ShouldDispatchThisFixedTick()
            {
                float ft = Time.fixedTime;
                if (sLastDispatchFixedTime == ft) return false;
                sLastDispatchFixedTime = ft;
                return true;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  PackJobInput — reads this instance's MonoBehaviour state into a
        //  blittable struct.  Called on the main thread before job dispatch.
        // ─────────────────────────────────────────────────────────────────
        private VehicleCalcInput PackJobInput()
        {
            float clampedRpm = Mathf.Clamp(smoothedRpm, 0f, Mathf.Max(1f, maxRpm));
            float normalizedRpm = Mathf.InverseLerp(
                Mathf.Max(0f, idleRpm),
                Mathf.Max(idleRpm + 1f, maxRpm),
                clampedRpm);

            return new VehicleCalcInput
            {
                smoothedRpm = smoothedRpm,
                smoothedLoad = smoothedLoad,
                previousSmoothedRpm = previousSmoothedRpm,
                previousSmoothedLoad = previousSmoothedLoad,
                clampedRpm = clampedRpm,
                normalizedRpm = normalizedRpm,
                idleRpm = idleRpm,
                maxRpm = maxRpm,
                idlePitch = idlePitch,
                loadEffectivenessOnPitch = loadEffectivenessOnPitch,
                targetedShiftPitch = targetedShiftPitch,
                shiftPitchOsc = shiftPitchOsc,
                loadCrossoverPoint = loadCrossoverPoint,
                loadBlendWidth = loadBlendWidth,
                autoBlip = autoBlip,
                launchMode = launchMode,
                nonDecelerateAudiosMode = nonDecelerateAudiosMode,
                idleVolume = idleVolume,
                masterVolume = masterVolume,
                loadVolumeAccChangerFactor = loadVolumeAccChangerFactor,
                loadVolumeDccChangerFactor = loadVolumeDccChangerFactor,
                loadVolumeChangerMinValue = loadVolumeChangerMinValue,
                maxVolumeAcc = maxVolumeAcc,
                maxVolumeDcc = maxVolumeDcc,
                acPitchTrim = acPitchTrim,
                dcPitchTrim = dcPitchTrim,
                accCount = accLayers.Count,
                decCount = decLayers.Count,
                combustionEventsPerRev = combustionEventsPerRev,
                lowPassIntensity = lowPassIntensity,
                lowPassStrength = lowPassStrength,
                mufflingIntensity = mufflingIntensity,
                highPassStrength = highPassStrength,
                resonanceStrength = resonanceStrength,
                distortionIntensity = distortionIntensity,
                distortionStrength = distortionStrength,
                chorusStrength = chorusStrength,
                reverbStrength = reverbStrength,

                // ── Carry hysteresis state from previous frame ───────────
                accStateInitialized = accBlendState.initialized,
                accStateLowIndex = accBlendState.lowIndex,
                accStateHighIndex = accBlendState.highIndex,
                accStateHoldUntilTime = accBlendState.holdUntilTime,

                decStateInitialized = decBlendState.initialized,
                decStateLowIndex = decBlendState.lowIndex,
                decStateHighIndex = decBlendState.highIndex,
                decStateHoldUntilTime = decBlendState.holdUntilTime,

                pairHysteresisRpm = pairHysteresisRpm,
                pairHoldCycles = pairHoldCycles,
                currentTime = Time.time
            };
        }

        // ─────────────────────────────────────────────────────────────────
        //  ApplyJobOutputs — main-thread Unity API writes after job complete.
        // ─────────────────────────────────────────────────────────────────
        private void ApplyJobData(VehicleCalcOutput o)
        {
            finalPitch = o.finalPitch;
            finalAccVol = o.finalAccVol;
            finalDecVol = o.finalDecVol;

            accBlendState.initialized = o.accStateInitialized;
            accBlendState.lowIndex = o.accStateLowIndex;
            accBlendState.highIndex = o.accStateHighIndex;
            accBlendState.holdUntilTime = o.accStateHoldUntilTime;
            decBlendState.initialized = o.decStateInitialized;
            decBlendState.lowIndex = o.decStateLowIndex;
            decBlendState.highIndex = o.decStateHighIndex;
            decBlendState.holdUntilTime = o.decStateHoldUntilTime;

            // Zero all targets then scatter the two active layers
            for (int i = 0; i < accTargetVolumes.Length; i++)
            { accTargetVolumes[i] = 0f; accTargetPitches[i] = 1f; }
            for (int i = 0; i < decTargetVolumes.Length; i++)
            { decTargetVolumes[i] = 0f; decTargetPitches[i] = 1f; }

            if (accLayers.Count > 0)
            {
                int lo = Mathf.Clamp(o.accLowIndex, 0, accLayers.Count - 1);
                int hi = Mathf.Clamp(o.accHighIndex, 0, accLayers.Count - 1);
                accTargetVolumes[lo] = o.accLowVolume; accTargetPitches[lo] = o.accLowPitch;
                if (lo != hi) { accTargetVolumes[hi] = o.accHighVolume; accTargetPitches[hi] = o.accHighPitch; }
            }
            if (decLayers.Count > 0)
            {
                int lo = Mathf.Clamp(o.decLowIndex, 0, decLayers.Count - 1);
                int hi = Mathf.Clamp(o.decHighIndex, 0, decLayers.Count - 1);
                decTargetVolumes[lo] = o.decLowVolume; decTargetPitches[lo] = o.decLowPitch;
                if (lo != hi) { decTargetVolumes[hi] = o.decHighVolume; decTargetPitches[hi] = o.decHighPitch; }
            }

            // Cache the output for ApplySlewAndEffects (called every tick)
            _lastJobOutput = o;
        }

        private void ApplyPrecomputedFilters(
            List<RuntimeLayer> bank, float[] targetVolumes,
            in VehicleCalcOutput o,
            float slewBase, float slew2000, float slew180, float slew2500)
        {
            if (bank == null) return;
            for (int i = 0; i < bank.Count; i++)
            {
                RuntimeLayer layer = bank[i];
                float tv = i < targetVolumes.Length ? targetVolumes[i] : 0f;
                float activity = Mathf.Max(layer.source.volume, tv);
                if (activity < 0.0005f) continue;

                layer.lowPass.cutoffFrequency =
                    Mathf.MoveTowards(layer.lowPass.cutoffFrequency, o.lowPassTarget, slew2000);
                layer.lowPass.lowpassResonanceQ =
                    Mathf.MoveTowards(layer.lowPass.lowpassResonanceQ, o.lowPassQTarget, slewBase);
                layer.highPass.cutoffFrequency =
                    Mathf.MoveTowards(layer.highPass.cutoffFrequency, o.highPassTarget, slew180);
                layer.highPass.highpassResonanceQ =
                    Mathf.MoveTowards(layer.highPass.highpassResonanceQ, o.highPassQTarget, slewBase);
                layer.distortion.distortionLevel =
                    Mathf.MoveTowards(layer.distortion.distortionLevel, o.distortionTarget, slewBase);
                layer.chorus.dryMix = Mathf.MoveTowards(layer.chorus.dryMix, o.chorusDryTarget, slewBase);
                layer.chorus.wetMix1 = Mathf.MoveTowards(layer.chorus.wetMix1, o.chorusWet * 0.6f, slewBase);
                layer.chorus.wetMix2 = Mathf.MoveTowards(layer.chorus.wetMix2, o.chorusWet * 0.3f, slewBase);
                layer.chorus.wetMix3 = Mathf.MoveTowards(layer.chorus.wetMix3, o.chorusWet * 0.1f, slewBase);
                layer.chorus.depth = Mathf.MoveTowards(layer.chorus.depth, o.chorusDepth, slewBase);
                layer.chorus.rate = Mathf.MoveTowards(layer.chorus.rate, o.chorusRate, slewBase);
                if (layer.reverb != null)
                {
                    layer.reverb.reverbLevel =
                        Mathf.MoveTowards(layer.reverb.reverbLevel, o.reverbLevel, slew2500);
                    layer.reverb.decayTime =
                        Mathf.MoveTowards(layer.reverb.decayTime, o.reverbDecay, slewBase);
                }
            }
        }

        private void ApplySlewAndEffects(float deltaTime)
        {
            ApplyTargetsToBank(accLayers, accTargetVolumes, accTargetPitches, deltaTime);
            ApplyTargetsToBank(decLayers, decTargetVolumes, decTargetPitches, deltaTime);

            float slewBase = deltaTime * FilterSlew;
            float slew2000 = slewBase * 2000f;
            float slew180 = slewBase * 180f;
            float slew2500 = slewBase * 2500f;

            ApplyPrecomputedFilters(accLayers, accTargetVolumes, _lastJobOutput,
                slewBase, slew2000, slew180, slew2500);
            ApplyPrecomputedFilters(decLayers, decTargetVolumes, _lastJobOutput,
                slewBase, slew2000, slew180, slew2500);

            if (useSharedMixerReverb && mixer != null && !string.IsNullOrEmpty(reverbMixerParamName))
                mixer.audioMixer.SetFloat(reverbMixerParamName, _lastJobOutput.reverbAmountDb);

            float clampedRpm = Mathf.Clamp(smoothedRpm, 0f, Mathf.Max(1f, maxRpm));
            UpdateBurble(deltaTime, clampedRpm);
            UpdateLugging(deltaTime, clampedRpm);
            UpdateDiagnostics(deltaTime);
        }

#endif

        [Header("Debug Controls")]
        [Space(5)]
        [Tooltip("Enable to manually control RPM and load values for testing.")]
        public bool debug = false;

        [Range(100f, 9000f)]
        [Tooltip("Test RPM value when debug mode is enabled.")]
        public float debugrpm = 800f;

        [Range(0.00f, 1.00f)]
        [Tooltip("Test engine load value when debug mode is enabled.")]
        public float debugload = 1f;

        [Tooltip("Enable the 1-second diagnostic logger.")]
        public bool enableDiagnosticLogger = false;

        [Tooltip("Enable burble gate diagnostics in the console.")]
        public bool enableBurbleDiagnostics = false;

        [Header("Core Audio Settings")]
        [Space(10)]
        [Tooltip("Fine-tune the overall pitch of engine sounds.")]
        public float targetedShiftPitch = 0f;

        [HideInInspector]
        public float shiftPitchOsc = 0f;

        [Range(0.007f, 1.00f)]
        [Tooltip("Master volume control for all engine sounds.")]
        public float masterVolume = 1f;

        [Range(0.000f, 1.00f)]
        [Tooltip("Depth of load-based volume modulation for the acceleration bank.")]
        public float loadVolumeAccChangerFactor = 1f;

        [Range(0.000f, 1.00f)]
        [Tooltip("Depth of load-based volume modulation for the deceleration bank.")]
        public float loadVolumeDccChangerFactor = 1f;

        [Range(0.00f, 0.99f)]
        [Tooltip("Gain floor applied when the bank is at its weakest load state.")]
        public float loadVolumeChangerMinValue = 0.1f;

        [Tooltip("Automatically force acc-bank blend to 1.0 when a sharp RPM jump is detected.")]
        public bool autoBlip = true;

        [Tooltip("Template AudioSource for copying base audio settings.")]
        public AudioSource audioSourceTemplate;

        [Tooltip("Optional mixer group for audio routing.")]
        public AudioMixerGroup mixer;

        [Tooltip("Label indicating this instance's role.")]
        public MixerType mixerType;

        [Tooltip("Fallback RPM spacing used when neighbouring clips are missing.")]
        public int rpmdeviation = 1000;

        [Header("Engine Sound Response Curves")]
        [Space(10)]
        [Tooltip("Global pitch contour vs normalized RPM.")]
        public AnimationCurve pitchCurve = new AnimationCurve(new Keyframe(0f, 0.97f), new Keyframe(1f, 1.03f));

        [Tooltip("How much engine load affects the global pitch contour.")]
        public float loadEffectivenessOnPitch = 0.05f;

        [Tooltip("Controls how bank loudness changes with normalized RPM.")]
        public AnimationCurve volumeCurve = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1f));

        [Header("Blend Behaviour")]
        [Space(5)]
        [Tooltip("Keep silent bank clips looping at zero volume.")]
        public bool keepBankClipsPlaying = true;

        [Range(0.005f, 0.50f)]
        [Tooltip("Per-layer volume smoothing response time in seconds.")]
        public float clipVolumeResponseTime = 0.05f;

        [Range(0.005f, 0.50f)]
        [Tooltip("Per-layer pitch smoothing response time in seconds.")]
        public float clipPitchResponseTime = 0.04f;

        [Range(0.005f, 0.50f)]
        [Tooltip("Input RPM smoothing response time in seconds.")]
        public float rpmResponseTime = 0.035f;

        [Range(0.005f, 0.50f)]
        [Tooltip("Input load smoothing response time in seconds.")]
        public float loadResponseTime = 0.04f;

        [Range(0f, 500f)]
        [Tooltip("Additional RPM margin required before the active neighbour pair is allowed to switch.")]
        public float pairHysteresisRpm = 120f;

        [Range(0f, 4f)]
        [Tooltip("Minimum hold duration after a pair switch, expressed in combustion-event cycles.")]
        public float pairHoldCycles = 0.5f;

        [Header("Combustion Timing")]
        [Space(5)]
        [Range(1, 16)]
        [Tooltip("Cylinder count used for combustion-event frequency estimation.")]
        public int cylinderCount = 4;

        [Tooltip("Four-stroke uses cylinderCount/2 firing events per crank revolution.")]
        public CombustionCycleMode combustionCycleMode = CombustionCycleMode.FourStroke;

        [Header("Audio Effects Configuration")]
        [Space(10)]
        [Tooltip("Controls distortion amount based on RPM and load.")]
        public AnimationCurve distortionCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.7f, 0.3f), new Keyframe(1f, 0.5f));

        [Range(0f, 1f)]
        [Tooltip("Overall intensity of the distortion effect.")]
        public float distortionIntensity = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("Intensity of the muffling effect when engine load decreases.")]
        public float mufflingIntensity = 0.5f;

        [Header("Low Pass Filter")]
        [Space(5)]
        [Tooltip("Controls low-pass frequency cutoff based on engine load.")]
        public AnimationCurve lowPassCurve = new AnimationCurve(
            new Keyframe(0f, 800f), new Keyframe(1f, 22000f));

        [Range(0f, 1f)]
        [Tooltip("Overall intensity of the low pass filter effect.")]
        public float lowPassIntensity = 0.5f;

        [Header("Exhaust Burble Configuration")]
        [Space(10)]
        [Tooltip("Enable exhaust burble/overrun effect.")]
        public bool enableExhaustBurble = true;

        [Tooltip("Audio clips for exhaust burble/overrun sounds.")]
        public AudioClip[] burbleSounds;

        [Range(0f, 1f)]
        [Tooltip("Master volume for burble sounds.")]
        public float burbleVolume = 0.7f;

        [Tooltip("Minimum RPM required for burble to occur.")]
        public float burbleMinRPM = 3500f;

        [Range(0f, 1f)]
        [Tooltip("How quickly load must drop to trigger burble.")]
        public float burbleLoadThreshold = 0.3f;

        [Tooltip("RPM drop per tick required to trigger burble via the RPM-drop path.")]
        [Range(0f, 3000f)]
        public float burbleRPMDropThreshold = 500f;

        [Range(0f, 1f)]
        [Tooltip("Chance of burble occurring when conditions are met.")]
        public float burbleProbability = 0.7f;

        [Range(0.01f, 0.5f)]
        [Tooltip("Minimum delay between burble sounds.")]
        public float minBurbleDelay = 0.05f;

        [Range(0.05f, 1f)]
        [Tooltip("Maximum delay between burble sounds.")]
        public float maxBurbleDelay = 0.2f;

        [Range(0f, 0.2f)]
        [Tooltip("Random pitch variation per burble event.")]
        public float burbleRandomPitchVariation = 0.08f;

        [Range(4f, 100f)]
        [Tooltip("Volume fade-out rate for burble sounds (units per second).")]
        public float burbleFadeRate = 40f;

        [Header("Engine Lugging Configuration")]
        [Space(10)]
        [Tooltip("Enable engine lugging/straining sound effect.")]
        public bool enableEngineLugging = true;

        [Tooltip("Audio clips for engine lugging sounds.")]
        public AudioClip[] luggingSounds;

        [Range(0f, 1f)]
        [Tooltip("Master volume for lugging sounds.")]
        public float luggingVolume = 0.8f;

        [Tooltip("Minimum RPM for lugging effect to start.")]
        public float luggingMinRPMThreshold = 800f;

        [Tooltip("Maximum RPM at which lugging effect can occur.")]
        public float luggingMaxRPMThreshold = 2000f;

        [Range(0f, 1f)]
        [Tooltip("Minimum engine load required to trigger lugging sound.")]
        public float luggingMinLoadThreshold = 0.7f;

        [Range(0.1f, 20f)]
        [Tooltip("Speed at which lugging sound fades in.")]
        public float luggingFadeInSpeed = 5f;

        [Range(0.1f, 20f)]
        [Tooltip("Speed at which lugging sound fades out.")]
        public float luggingFadeOutSpeed = 3f;

        [Range(0.5f, 1.5f)]
        [Tooltip("Base pitch for lugging sounds.")]
        public float luggingBasePitch = 0.9f;

        [Range(0f, 0.2f)]
        [Tooltip("Random pitch variation for lugging clips.")]
        public float luggingRandomPitchVariation = 0.05f;

        [Header("Audio Clip Configuration")]
        [Space(10)]
        [SerializeField]
        [Tooltip("List of audio clips for engine acceleration.")]
        public List<EngineAudioClipData> acceleratingSounds = new List<EngineAudioClipData>();

        [SerializeField]
        [Tooltip("Optional list of audio clips for engine deceleration.")]
        public List<EngineAudioClipData> deceleratingSounds = new List<EngineAudioClipData>();

        [Header("Volume Configuration")]
        [Space(10)]
        [Range(0.05f, 1.00f)]
        [Tooltip("Default volume when engine is idle.")]
        public float idleVolume = 0.1f;

        [Range(0.00f, 2.00f)]
        [Tooltip("Maximum volume for acceleration sounds (0 = no limit).")]
        public float maxVolumeAcc = 0.4f;

        [Range(0.00f, 2.00f)]
        [Tooltip("Maximum volume for deceleration sounds (0 = no limit).")]
        public float maxVolumeDcc = 0.1f;

        [Header("Fine-Tuning")]
        [Space(10)]
        [Range(-1.0f, 1.0f)]
        [Tooltip("Fine-tune pitch for acceleration sounds.")]
        public float acPitchTrim = 0f;

        [Range(-1.0f, 1.0f)]
        [Tooltip("Fine-tune pitch for deceleration sounds.")]
        public float dcPitchTrim = 0f;

        [Range(1000f, 20000f)]
        [Tooltip("Maximum theoretical RPM value for calculations.")]
        public float maximumTheoricalRPM = 10000f;

        [Range(0.5f, 2f)]
        [Tooltip("Base pitch value when engine is near idle.")]
        public float idlePitch = 1f;

        [HideInInspector]
        public bool launchMode = false;

        [Header("Sound Character - Strength Controls")]
        [Range(0f, 1f)]
        [Tooltip("Low-pass muffling boost.")]
        public float lowPassStrength = 0f;

        [Range(0f, 1f)]
        [Tooltip("High-pass rasp strength.")]
        public float highPassStrength = 0f;

        [Range(0f, 1f)]
        [Tooltip("Resonance strength.")]
        public float resonanceStrength = 0f;

        [Range(0f, 1f)]
        [Tooltip("Distortion boost multiplier.")]
        public float distortionStrength = 0f;

        [Range(0f, 1f)]
        [Tooltip("Chorus strength.")]
        public float chorusStrength = 0f;

        [Range(0f, 1f)]
        [Tooltip("Reverb strength.")]
        public float reverbStrength = 0f;

        [Tooltip("When true, per-layer AudioReverbFilter components are NOT created.")]
        public bool useSharedMixerReverb = true;

        [Tooltip("Name of the exposed float parameter on the mixer that controls reverb send/wet level.")]
        public string reverbMixerParamName = "ReverbAmount";

        [Header("Acc/Dec Crossfade")]
        [Space(5)]
        [Range(0f, 0.5f)]
        [Tooltip("Load value at which acceleration and deceleration layers are blended 50/50.")]
        public float loadCrossoverPoint = 0.18f;

        [Range(0.01f, 0.4f)]
        [Tooltip("Width of the blend zone around loadCrossoverPoint.")]
        public float loadBlendWidth = 0.10f;

        private float _rpm;
        private float _load;
        private float _maxRpm;
        private bool _isOn = false;
        private float _idleRpm;
        private float finalPitch = 1f;
        private float finalAccVol = 0f;
        private float finalDecVol = 0f;
        private float[] AcMinrTable;
        private float[] AcNormalrTable;
        private float[] AcMaxrTable;
        private float[] DcMinrTable;
        private float[] DcNormalrTable;
        private float[] DcMaxrTable;
        private bool nonDecelerateAudiosMode = true;
        private float combustionEventsPerRev = 2f;
        private List<AudioSource> luggingAudioSources;
        private const int LUGGINGAUDIOPOOLSIZE = 2;
        private const int BURBLEAUDIOPOOLSIZE = 5;
        private const float FilterSlew = 16f;
        private readonly List<RuntimeLayer> accLayers = new List<RuntimeLayer>();
        private readonly List<RuntimeLayer> decLayers = new List<RuntimeLayer>();
        private readonly List<AudioSource> burbleAudioSources = new List<AudioSource>();
        private float[] accTargetVolumes = Array.Empty<float>();
        private float[] accTargetPitches = Array.Empty<float>();
        private float[] decTargetVolumes = Array.Empty<float>();
        private float[] decTargetPitches = Array.Empty<float>();
        private Coroutine calculationRoutine;
        private float smoothedRpm;
        private float smoothedLoad;
        private float previousSmoothedRpm;
        private float previousSmoothedLoad;
        private float lastPitchWarningTime;
        private float nextBurbleTime;
        private float currentLuggingVolume;
        private int activeLuggingIndex = -1;
        private float currentLuggingPitchRandom;
        private float diagnosticTimer;
        private BankBlendState accBlendState;
        private BankBlendState decBlendState;
        public float rpm
        {
            get => _rpm;
            set => _rpm = Mathf.Clamp(value, 0f, Mathf.Max(_maxRpm, maximumTheoricalRPM, 1f));
        }

        public float load
        {
            get => _load;
            set => _load = Mathf.Clamp01(value);
        }

        public float maxRpm
        {
            get => _maxRpm;
            set => _maxRpm = Mathf.Max(1f, value);
        }

        public float idleRpm
        {
            get => _idleRpm;
            set => _idleRpm = Mathf.Clamp(value, 0f, Mathf.Max(0f, _maxRpm - 1f));
        }

        public bool isOn
        {
            get => _isOn;
            set
            {
                _isOn = value;
                if (!_isOn)
                {
                    for (int i = 0; i < accLayers.Count; i++) accLayers[i].source.volume = 0f;
                    for (int i = 0; i < decLayers.Count; i++) decLayers[i].source.volume = 0f;
                }
            }
        }

        public void Activate()
        {
            if (!Application.isPlaying || !gameObject.scene.IsValid() || !isActiveAndEnabled) return;
            BuildRuntimeConfiguration();
            BakeAllCurves();
            RebuildAllAudioSources();
            StartProcessing();
#if !UNITY_WEBGL
            VehicleNoiseBatchManager.Register(this);
#endif
        }

        public void Deactivate()
        {
#if !UNITY_WEBGL
            VehicleNoiseBatchManager.Unregister(this);
#endif
            StopProcessing();
            DestroyAllRuntimeSources();
        }

        public void SetEngineState(float currentRpm, float currentLoad, bool engineOn)
        {
            rpm = currentRpm;
            load = currentLoad;
            isOn = engineOn;
        }

        public void SetRPM(float currentRpm) => rpm = currentRpm;
        public void SetLoad(float currentLoad) => load = currentLoad;
        public void TurnOn() => isOn = true;
        public void TurnOff() => isOn = false;

        private void Awake()
        {
            BuildRuntimeConfiguration();
            BakeAllCurves();
        }

        private void OnEnable() => Activate();
        private void OnDisable() => Deactivate();
        private void OnDestroy() => Deactivate();

        private void OnValidate()
        {
            cylinderCount = Mathf.Max(1, cylinderCount);
            rpmdeviation = Mathf.Max(1, rpmdeviation);
            maximumTheoricalRPM = Mathf.Max(1000f, maximumTheoricalRPM);
            loadVolumeChangerMinValue = Mathf.Clamp(loadVolumeChangerMinValue, 0f, 0.99f);
            loadBlendWidth = Mathf.Max(0.01f, loadBlendWidth);
            clipVolumeResponseTime = Mathf.Max(0.005f, clipVolumeResponseTime);
            clipPitchResponseTime = Mathf.Max(0.005f, clipPitchResponseTime);
            rpmResponseTime = Mathf.Max(0.005f, rpmResponseTime);
            loadResponseTime = Mathf.Max(0.005f, loadResponseTime);
            pairHysteresisRpm = Mathf.Max(0f, pairHysteresisRpm);
            pairHoldCycles = Mathf.Max(0f, pairHoldCycles);
            combustionEventsPerRev = ComputeCombustionEventsPerRevolution();
            NormalizeClipSettings(acceleratingSounds);
            NormalizeClipSettings(deceleratingSounds);
            SortClipLists();

            if (!Application.isPlaying) { BuildRuntimeConfiguration(); BakeAllCurves(); return; }
            BakeAllCurves();
            Activate();
        }

        private void NormalizeClipSettings(List<EngineAudioClipData> clips)
        {
            if (clips == null) return;
            for (int i = 0; i < clips.Count; i++)
            {
                EngineAudioClipData clip = clips[i];
                if (clip == null) continue;
                clip.rpmValue = Mathf.Max(0, clip.rpmValue);
                clip.rpmPitchTracking = Mathf.Clamp01(clip.rpmPitchTracking);
                clip.minPitch = Mathf.Clamp(clip.minPitch, 0.01f, 10f);
                clip.maxPitch = Mathf.Clamp(clip.maxPitch, clip.minPitch, 10f);
            }
        }

        private void BuildRuntimeConfiguration()
        {
            combustionEventsPerRev = ComputeCombustionEventsPerRevolution();
            SortClipLists();
            BuildRpmTables(acceleratingSounds, out AcMinrTable, out AcNormalrTable, out AcMaxrTable);
            BuildRpmTables(deceleratingSounds, out DcMinrTable, out DcNormalrTable, out DcMaxrTable);
            nonDecelerateAudiosMode = deceleratingSounds == null || deceleratingSounds.Count == 0;

            float highestClipRpm = 0f;
            if (AcNormalrTable != null && AcNormalrTable.Length > 0)
                highestClipRpm = Mathf.Max(highestClipRpm, AcNormalrTable[AcNormalrTable.Length - 1]);
            if (DcNormalrTable != null && DcNormalrTable.Length > 0)
                highestClipRpm = Mathf.Max(highestClipRpm, DcNormalrTable[DcNormalrTable.Length - 1]);

            _maxRpm = Mathf.Max(maximumTheoricalRPM, highestClipRpm, 1f);
            bool idleRpmNeedsRefresh = _idleRpm <= 0f || _idleRpm >= _maxRpm;
            _idleRpm = Mathf.Clamp(idleRpmNeedsRefresh ? InferIdleRpm() : _idleRpm, 0f, _maxRpm);
        }

        private void SortClipLists()
        {
            if (acceleratingSounds == null) acceleratingSounds = new List<EngineAudioClipData>();
            if (deceleratingSounds == null) deceleratingSounds = new List<EngineAudioClipData>();
            acceleratingSounds = acceleratingSounds.Where(c => c != null && c.audioClip != null).OrderBy(c => c.rpmValue).ToList();
            deceleratingSounds = deceleratingSounds.Where(c => c != null && c.audioClip != null).OrderBy(c => c.rpmValue).ToList();
        }

        private void BuildRpmTables(List<EngineAudioClipData> clips, out float[] minTable, out float[] normalTable, out float[] maxTable)
        {
            if (clips == null || clips.Count == 0) { minTable = Array.Empty<float>(); normalTable = Array.Empty<float>(); maxTable = Array.Empty<float>(); return; }
            int count = clips.Count;
            minTable = new float[count];
            normalTable = new float[count];
            maxTable = new float[count];

            for (int i = 0; i < count; i++) normalTable[i] = Mathf.Max(1f, clips[i].rpmValue);

            for (int i = 0; i < count; i++)
            {
                float current = normalTable[i];
                float previous = i > 0 ? normalTable[i - 1] : current - rpmdeviation;
                float next = i < count - 1 ? normalTable[i + 1] : current + rpmdeviation;
                minTable[i] = Mathf.Max(0f, i == 0 ? previous + (current - previous) * 0.5f : current - (next - current) * 0.5f);
                maxTable[i] = Mathf.Max(current, i == count - 1 ? current + (current - previous) * 0.5f : current + (next - current) * 0.5f);
                if (i == 0) minTable[i] = Mathf.Max(0f, current - Mathf.Max(1f, maxTable[i] - current));
                if (i == count - 1) maxTable[i] = Mathf.Max(current, current + Mathf.Max(1f, current - minTable[i]));
            }

#if UNITY_EDITOR
            if (count >= 2)
            {
                float minSpacing = float.MaxValue, maxSpacing = 0f;
                for (int i = 1; i < count; i++)
                {
                    float spacing = normalTable[i] - normalTable[i - 1];
                    minSpacing = Mathf.Min(minSpacing, spacing);
                    maxSpacing = Mathf.Max(maxSpacing, spacing);
                }
                if (rpmdeviation < minSpacing * 0.5f || rpmdeviation > maxSpacing * 2f)
                    Debug.LogWarning($"VNS: rpmdeviation ({rpmdeviation}) is far from actual clip spacing (min={minSpacing:0}, max={maxSpacing:0}). Edge blend zones may be misleading.", this);
            }
#endif
        }

        private void RebuildAllAudioSources()
        {
            DestroyAllRuntimeSources();
            luggingAudioSources = new List<AudioSource>();
            BuildBankLayers(acceleratingSounds, accLayers, "ACC");
            BuildBankLayers(deceleratingSounds, decLayers, "DEC");
            BuildBurblePool();
            BuildLuggingPool();
            accTargetVolumes = new float[accLayers.Count];
            accTargetPitches = new float[accLayers.Count];
            decTargetVolumes = new float[decLayers.Count];
            decTargetPitches = new float[decLayers.Count];
            accBlendState = default;
            decBlendState = default;
        }

        private void BuildBankLayers(List<EngineAudioClipData> clips, List<RuntimeLayer> runtime, string prefix)
        {
            runtime.Clear();
            if (clips == null) return;
            for (int i = 0; i < clips.Count; i++)
            {
                EngineAudioClipData clipData = clips[i];
                if (clipData == null || clipData.audioClip == null) continue;
                GameObject host = new GameObject($"{prefix}{i:00}_{clipData.rpmValue}");
                host.transform.SetParent(transform, false);
                AudioSource source = host.AddComponent<AudioSource>();
                ApplyTemplateToAudioSource(source);
                source.clip = clipData.audioClip;
                source.loop = true;
                source.playOnAwake = false;
                source.volume = 0f;
                source.pitch = 1f;
                source.mute = false;

                RuntimeLayer layer = new RuntimeLayer
                {
                    host = host,
                    source = source,
                    lowPass = host.AddComponent<AudioLowPassFilter>(),
                    highPass = host.AddComponent<AudioHighPassFilter>(),
                    distortion = host.AddComponent<AudioDistortionFilter>(),
                    chorus = host.AddComponent<AudioChorusFilter>(),
                    reverb = (useSharedMixerReverb && mixer != null) ? null : host.AddComponent<AudioReverbFilter>(),
                    clip = clipData.audioClip,
                    referenceRpm = Mathf.Max(1f, clipData.rpmValue),
                    rpmPitchTracking = clipData.rpmPitchTracking,
                    minPitch = clipData.minPitch,
                    maxPitch = clipData.maxPitch,
                    volumeOffset = clipData.volumeOffset,
                    pitchOffset = clipData.pitchOffset
                };
                InitializeFilterDefaults(layer);
                runtime.Add(layer);
                if (keepBankClipsPlaying) source.Play();
            }
        }

        private void BuildBurblePool()
        {
            burbleAudioSources.Clear();
            for (int i = 0; i < BURBLEAUDIOPOOLSIZE; i++)
            {
                GameObject host = new GameObject($"BURBLE{i:00}");
                host.transform.SetParent(transform, false);
                AudioSource source = host.AddComponent<AudioSource>();
                ApplyTemplateToAudioSource(source);
                source.loop = false;
                source.playOnAwake = false;
                source.volume = 0f;
                burbleAudioSources.Add(source);
            }
        }

        private void BuildLuggingPool()
        {
            for (int i = 0; i < LUGGINGAUDIOPOOLSIZE; i++)
            {
                GameObject host = new GameObject($"LUGGING{i:00}");
                host.transform.SetParent(transform, false);
                AudioSource source = host.AddComponent<AudioSource>();
                ApplyTemplateToAudioSource(source);
                source.loop = true;
                source.playOnAwake = false;
                source.volume = 0f;
                source.pitch = luggingBasePitch;
                luggingAudioSources.Add(source);
            }
        }

        private void InitializeFilterDefaults(RuntimeLayer layer)
        {
            layer.lowPass.enabled = true;
            layer.lowPass.cutoffFrequency = 22000f;
            layer.lowPass.lowpassResonanceQ = 1f;
            layer.highPass.enabled = true;
            layer.highPass.cutoffFrequency = 10f;
            layer.highPass.highpassResonanceQ = 1f;
            layer.distortion.enabled = true;
            layer.distortion.distortionLevel = 0f;
            layer.chorus.enabled = true;
            layer.chorus.dryMix = 1f;
            layer.chorus.wetMix1 = 0f;
            layer.chorus.wetMix2 = 0f;
            layer.chorus.wetMix3 = 0f;
            layer.chorus.delay = 20f;
            layer.chorus.rate = 0.8f;
            layer.chorus.depth = 0f;
            if (layer.reverb != null)
            {
                layer.reverb.enabled = true;
                layer.reverb.reverbLevel = -10000f;
                layer.reverb.decayTime = 1f;
                layer.reverb.diffusion = 100f;
                layer.reverb.density = 100f;
            }
        }

        private void StartProcessing()
        {
            if (!isActiveAndEnabled) return;
            StopProcessing();
            smoothedRpm = _rpm;
            smoothedLoad = _load;
            previousSmoothedRpm = smoothedRpm;
            previousSmoothedLoad = smoothedLoad;
            calculationRoutine = StartCoroutine(CalculateAsync());
        }

        private void StopProcessing()
        {
            if (calculationRoutine != null) { StopCoroutine(calculationRoutine); calculationRoutine = null; }
        }

        private IEnumerator CalculateAsync()
        {
            var waitFixed = new WaitForFixedUpdate();
            while (true)
            {
                yield return waitFixed;
                if (!_isOn) { FadeAllToSilence(Time.fixedDeltaTime); continue; }

                float deltaTime = Time.fixedDeltaTime;
                float rpmAlpha = EvaluateSmoothingAlpha(rpmResponseTime, deltaTime);
                float loadAlpha = EvaluateSmoothingAlpha(loadResponseTime, deltaTime);
                previousSmoothedRpm = smoothedRpm;
                previousSmoothedLoad = smoothedLoad;
                float targetRpm = debug ? debugrpm : _rpm;
                float targetLoad = debug ? debugload : _load;
                smoothedRpm = Mathf.Lerp(smoothedRpm, targetRpm, rpmAlpha);
                smoothedLoad = Mathf.Lerp(smoothedLoad, targetLoad, loadAlpha);

#if UNITY_WEBGL
                ProcessAudioFrame(deltaTime);
#else
                // Run the batch job (only the first synthesizer per fixed tick
                // actually dispatches; the rest no-op safely because the batch
                // already processed all instances).
                VehicleNoiseBatchManager.ExecuteBatchIfNeeded(deltaTime);

                // ALWAYS apply the volume/pitch slew to AudioSource every fixed
                // tick regardless of whether this instance triggered the batch.
                // The target arrays were written by the job; the Lerp must advance
                // every tick or audio stalls when physics outruns rendering.
                ApplySlewAndEffects(deltaTime);
#endif
            }
        }

        private void ProcessAudioFrame(float deltaTime)
        {
            float clampedRpm = Mathf.Clamp(smoothedRpm, 0f, Mathf.Max(1f, _maxRpm));
            float normalizedRpm = Mathf.InverseLerp(Mathf.Max(0f, _idleRpm), Mathf.Max(_idleRpm + 1f, _maxRpm), clampedRpm);

            // [fix] Smooth idlePitch blend like idleVolume: Lerp(idlePitch, curve, normalizedRpm)
            float curvePitch = SampleBakedCurve(bakedPitchCurve, normalizedRpm);
            float pitchShape = Mathf.Lerp(idlePitch, curvePitch, normalizedRpm);
            float loadPitchContrib = smoothedLoad * loadEffectivenessOnPitch;
            finalPitch = Mathf.Max(0.01f, pitchShape + loadPitchContrib + targetedShiftPitch + shiftPitchOsc);

            float halfWidth = Mathf.Max(0.005f, loadBlendWidth * 0.5f);
            float start = Mathf.Clamp01(loadCrossoverPoint - halfWidth);
            float end = Mathf.Clamp01(loadCrossoverPoint + halfWidth);
            float tLoad = Mathf.Clamp01(Mathf.InverseLerp(start, end, smoothedLoad));
            float accBlend = tLoad * tLoad * (3f - 2f * tLoad);

            if (autoBlip)
            {
                bool rpmJumpUp = previousSmoothedRpm + 75f < clampedRpm;
                bool snappedIdle = clampedRpm <= Mathf.Max(1f, _idleRpm) && previousSmoothedRpm > clampedRpm + 75f;
                if (rpmJumpUp || snappedIdle) accBlend = 1f;
            }

            float decBlend = 1f - accBlend;
            float rpmVolume = SampleBakedCurve(bakedVolumeCurve, normalizedRpm);
            float idleBias = Mathf.Lerp(idleVolume, 1f, normalizedRpm);
            finalAccVol = Mathf.Clamp01(accBlend * rpmVolume * idleBias);
            finalDecVol = Mathf.Clamp01(decBlend * rpmVolume * idleBias);

            if (launchMode) { finalAccVol = 1f; finalDecVol = 0f; }
            else if (nonDecelerateAudiosMode)
            {
                finalAccVol = Mathf.Clamp01(Mathf.Max(finalAccVol, finalDecVol));
                finalDecVol = 0f;
            }
            if (clampedRpm <= Mathf.Max(1f, _idleRpm))
                finalAccVol = Mathf.Max(finalAccVol, idleVolume);

            EvaluateBankTargets(accLayers, accTargetVolumes, accTargetPitches, finalPitch, finalAccVol, clampedRpm, maxVolumeAcc, acPitchTrim, ref accBlendState);
            EvaluateBankTargets(decLayers, decTargetVolumes, decTargetPitches, finalPitch, finalDecVol, clampedRpm, maxVolumeDcc, dcPitchTrim, ref decBlendState);

            float deltaTimeAdjusted = Time.fixedDeltaTime;
            ApplyTargetsToBank(accLayers, accTargetVolumes, accTargetPitches, deltaTimeAdjusted);
            ApplyTargetsToBank(decLayers, decTargetVolumes, decTargetPitches, deltaTimeAdjusted);
            ApplyPostEffectsToBank(accLayers, accTargetVolumes, normalizedRpm, deltaTimeAdjusted);
            ApplyPostEffectsToBank(decLayers, decTargetVolumes, normalizedRpm, deltaTimeAdjusted);
            UpdateBurble(deltaTimeAdjusted, clampedRpm);
            UpdateLugging(deltaTimeAdjusted, clampedRpm);
            UpdateDiagnostics(deltaTimeAdjusted);
        }

        private void EvaluateBankTargets(
            List<RuntimeLayer> bank, float[] targetVolumes, float[] targetPitches,
            float finalPitch, float bankFinalVol, float clampedRpm,
            float bankVolumeLimit, float bankPitchTrim, ref BankBlendState blendState)
        {
            int count = bank.Count;
            for (int i = 0; i < count; i++) { targetVolumes[i] = 0f; targetPitches[i] = 1f; }
            if (count == 0) return;

            float loadGainRaw = bankVolumeLimit == maxVolumeAcc
                ? Mathf.Lerp(loadVolumeChangerMinValue, 1f, smoothedLoad)
                : Mathf.Lerp(loadVolumeChangerMinValue, 1f, 1f - smoothedLoad);
            float loadGainFactor = bankVolumeLimit == maxVolumeAcc ? loadVolumeAccChangerFactor : loadVolumeDccChangerFactor;
            float loadGain = Mathf.Lerp(1f, loadGainRaw, loadGainFactor);
            float bankBaseGain = masterVolume * bankFinalVol * Mathf.Max(0f, loadGain);

            FindStableNeighbourPair(bank, clampedRpm, ref blendState, out int lo, out int hi);

            if (lo == hi)
            {
                RuntimeLayer single = bank[lo];
                float g = bankBaseGain * Mathf.Max(0f, 1f + single.volumeOffset);
                if (bankVolumeLimit > 0f) g = Mathf.Min(g, bankVolumeLimit);
                targetVolumes[lo] = g;
                targetPitches[lo] = EvaluateLayerPitch(single, clampedRpm, finalPitch, bankPitchTrim, _maxRpm);
                return;
            }

            float t = Mathf.InverseLerp(bank[lo].referenceRpm, bank[hi].referenceRpm, clampedRpm);
            // Constant-power crossfade: cos² + sin² = 1 (standard pan law)
            float angle = t * Mathf.PI * 0.5f;
            float lowW = Mathf.Cos(angle);
            float highW = Mathf.Sin(angle);

            RuntimeLayer lLo = bank[lo], lHi = bank[hi];
            float gLo = bankBaseGain * lowW * Mathf.Max(0f, 1f + lLo.volumeOffset);
            float gHi = bankBaseGain * highW * Mathf.Max(0f, 1f + lHi.volumeOffset);

            if (bankVolumeLimit > 0f) { gLo = Mathf.Min(gLo, bankVolumeLimit); gHi = Mathf.Min(gHi, bankVolumeLimit); }

            targetVolumes[lo] = gLo;
            targetVolumes[hi] = gHi;
            targetPitches[lo] = EvaluateLayerPitch(lLo, clampedRpm, finalPitch, bankPitchTrim, _maxRpm);
            targetPitches[hi] = EvaluateLayerPitch(lHi, clampedRpm, finalPitch, bankPitchTrim, _maxRpm);
        }

        private float EvaluateLayerPitch(RuntimeLayer layer, float clampedRpm, float finalPitch, float bankPitchTrim, float maxRpm)
        {
            // [fix] Pitch = RPM-based progress mapped to [minPitch, maxPitch].
            // Continuous across pair boundaries.
            float progress = Mathf.Clamp01(clampedRpm / Mathf.Max(1f, maxRpm));
            float pitch = Mathf.Lerp(layer.minPitch, layer.maxPitch, progress);
            pitch += (finalPitch - 1f) + bankPitchTrim + layer.pitchOffset;
            return Mathf.Clamp(pitch, 0.01f, 10f);
        }

        private void FindStableNeighbourPair(List<RuntimeLayer> bank, float clampedRpm, ref BankBlendState state, out int lo, out int hi)
        {
            int count = bank.Count;
            if (count == 1) { lo = hi = 0; return; }

            FindImmediateNeighbourPair(bank, clampedRpm, out int immLo, out int immHi);

            if (!state.initialized) { state.initialized = true; state.lowIndex = immLo; state.highIndex = immHi; state.holdUntilTime = 0f; lo = immLo; hi = immHi; return; }

            bool sameAsCurrent = immLo == state.lowIndex && immHi == state.highIndex;
            if (sameAsCurrent) { lo = state.lowIndex; hi = state.highIndex; return; }

            float currentTime = Time.time;
            if (currentTime < state.holdUntilTime) { lo = state.lowIndex; hi = state.highIndex; return; }

            bool rpmPassedMarginLow = clampedRpm < bank[state.lowIndex].referenceRpm - pairHysteresisRpm;
            bool rpmPassedMarginHigh = clampedRpm > bank[state.highIndex].referenceRpm + pairHysteresisRpm;
            if (!rpmPassedMarginLow && !rpmPassedMarginHigh) { lo = state.lowIndex; hi = state.highIndex; return; }

            float cycleFrequency = Mathf.Max(0.1f, smoothedRpm / 60f * combustionEventsPerRev);
            float holdDuration = pairHoldCycles / cycleFrequency;
            state.lowIndex = immLo;
            state.highIndex = immHi;
            state.holdUntilTime = currentTime + holdDuration;
            lo = state.lowIndex;
            hi = state.highIndex;
        }

        private void FindImmediateNeighbourPair(List<RuntimeLayer> bank, float clampedRpm, out int lo, out int hi)
        {
            int count = bank.Count;
            if (count == 1) { lo = hi = 0; return; }

            for (int i = 0; i < count - 1; i++)
            {
                if (clampedRpm >= bank[i].referenceRpm && clampedRpm <= bank[i + 1].referenceRpm)
                {
                    lo = i; hi = i + 1; return;
                }
            }

            if (clampedRpm < bank[0].referenceRpm) { lo = hi = 0; return; }
            if (clampedRpm > bank[count - 1].referenceRpm) { lo = hi = count - 1; return; }

            lo = hi = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float d = Mathf.Abs(bank[i].referenceRpm - clampedRpm);
                if (d < bestDist) { bestDist = d; lo = hi = i; }
            }
        }

        private void ApplyTargetsToBank(List<RuntimeLayer> bank, float[] targetVolumes, float[] targetPitches, float deltaTime)
        {
            if (bank == null) return;
            float volumeSlew = EvaluateSmoothingAlpha(clipVolumeResponseTime, deltaTime);
            float pitchSlew = EvaluateSmoothingAlpha(clipPitchResponseTime, deltaTime);

            for (int i = 0; i < bank.Count; i++)
            {
                RuntimeLayer layer = bank[i];
                float tv = i < targetVolumes.Length ? targetVolumes[i] : 0f;
                float tp = i < targetPitches.Length ? targetPitches[i] : 1f;
                layer.source.volume = Mathf.Lerp(layer.source.volume, tv, volumeSlew);
                layer.source.pitch = Mathf.Lerp(layer.source.pitch, tp, pitchSlew);

                if (!layer.source.isPlaying)
                {
                    if (layer.source.volume > 0.001f) layer.source.Play();
                }
                else if (!keepBankClipsPlaying && layer.source.volume < 0.001f)
                {
                    layer.source.volume = 0f;
                    layer.source.Stop();
                }
            }
        }

        private void ApplyPostEffectsToBank(List<RuntimeLayer> bank, float[] targetVolumes, float normalizedRpm, float deltaTime)
        {
            if (bank == null) return;

            float lpCurveValue = Mathf.Clamp(SampleBakedCurve(bakedLowPassCurve, smoothedLoad), 500f, 22000f);
            float lpMix = Mathf.Clamp01(Mathf.Max(lowPassIntensity, lowPassStrength + mufflingIntensity));
            float lowPassTarget = Mathf.Lerp(22000f, lpCurveValue, lpMix);
            float hpAmount = Mathf.Clamp01(normalizedRpm * normalizedRpm * highPassStrength);
            float highPassTarget = Mathf.Lerp(10f, 1800f, hpAmount);
            float resShape = Mathf.Sin(normalizedRpm * Mathf.PI);
            float lowPassQ = Mathf.Lerp(1f, 8f, Mathf.Clamp01(resShape * resonanceStrength));
            float highPassQ = Mathf.Lerp(1f, 2.2f, hpAmount);
            float distDrive = SampleBakedCurve(bakedDistortionCurve, normalizedRpm) * (smoothedLoad + 0.5f);
            float distortionTarget = Mathf.Clamp01(distDrive * distortionIntensity * (1f + distortionStrength));
            float chorusAmount = Mathf.Clamp01(Mathf.InverseLerp(0.3f, 1f, normalizedRpm) * chorusStrength);
            float chorusWet = Mathf.Lerp(0f, 0.55f, chorusAmount);
            float chorusDepth = Mathf.Lerp(0f, 0.7f, chorusAmount);
            float chorusRate = Mathf.Lerp(0.8f, 2.1f, chorusAmount);
            float totalWet = chorusWet * 0.6f + chorusWet * 0.3f + chorusWet * 0.1f;
            float chorusDry = Mathf.Max(0f, 1f - totalWet);
            float reverbAmount = Mathf.Clamp01(Mathf.Max(normalizedRpm, smoothedLoad) * reverbStrength);
            float reverbLevel = Mathf.Lerp(-10000f, -1000f, reverbAmount);
            float reverbDecay = Mathf.Lerp(0.8f, 2.3f, reverbAmount);
            float reverbAmountDb = Mathf.Lerp(-80f, 0f, reverbAmount);

            float slewBase = deltaTime * FilterSlew;
            float slew2000 = slewBase * 2000f;
            float slew180 = slewBase * 180f;
            float slew2500 = slewBase * 2500f;

            for (int i = 0; i < bank.Count; i++)
            {
                RuntimeLayer layer = bank[i];
                float tv = i < targetVolumes.Length ? targetVolumes[i] : 0f;
                float activity = Mathf.Max(layer.source.volume, tv);
                if (activity < 0.0005f) continue;

                layer.lowPass.cutoffFrequency = Mathf.MoveTowards(layer.lowPass.cutoffFrequency, lowPassTarget, slew2000);
                layer.lowPass.lowpassResonanceQ = Mathf.MoveTowards(layer.lowPass.lowpassResonanceQ, lowPassQ, slewBase);
                layer.highPass.cutoffFrequency = Mathf.MoveTowards(layer.highPass.cutoffFrequency, highPassTarget, slew180);
                layer.highPass.highpassResonanceQ = Mathf.MoveTowards(layer.highPass.highpassResonanceQ, highPassQ, slewBase);
                layer.distortion.distortionLevel = Mathf.MoveTowards(layer.distortion.distortionLevel, distortionTarget, slewBase);
                layer.chorus.dryMix = Mathf.MoveTowards(layer.chorus.dryMix, chorusDry, slewBase);
                layer.chorus.wetMix1 = Mathf.MoveTowards(layer.chorus.wetMix1, chorusWet * 0.6f, slewBase);
                layer.chorus.wetMix2 = Mathf.MoveTowards(layer.chorus.wetMix2, chorusWet * 0.3f, slewBase);
                layer.chorus.wetMix3 = Mathf.MoveTowards(layer.chorus.wetMix3, chorusWet * 0.1f, slewBase);
                layer.chorus.depth = Mathf.MoveTowards(layer.chorus.depth, chorusDepth, slewBase);
                layer.chorus.rate = Mathf.MoveTowards(layer.chorus.rate, chorusRate, slewBase);
                if (layer.reverb != null)
                {
                    layer.reverb.reverbLevel = Mathf.MoveTowards(layer.reverb.reverbLevel, reverbLevel, slew2500);
                    layer.reverb.decayTime = Mathf.MoveTowards(layer.reverb.decayTime, reverbDecay, slewBase);
                }
            }

            if (useSharedMixerReverb && mixer != null && !string.IsNullOrEmpty(reverbMixerParamName))
                mixer.audioMixer.SetFloat(reverbMixerParamName, reverbAmountDb);
        }

        private static float EvaluateSmoothingAlpha(float responseTime, float deltaTime)
        {
            if (responseTime < 0.0001f) return 1f;
            return 1f - Mathf.Exp(-deltaTime / responseTime);
        }

        private void UpdateBurble(float deltaTime, float clampedRpm)
        {
            if (!enableExhaustBurble || burbleSounds == null || burbleSounds.Length == 0) return;

            for (int i = 0; i < burbleAudioSources.Count; i++)
            {
                AudioSource src = burbleAudioSources[i];
                if (src.isPlaying) src.volume = Mathf.Max(0f, src.volume - burbleFadeRate * deltaTime);
            }

            if (Time.time < nextBurbleTime) return;

            bool rpmCondition = clampedRpm >= burbleMinRPM;
            bool loadCondition = smoothedLoad < burbleLoadThreshold;
            bool rpmDropCond = burbleRPMDropThreshold > 0f && (previousSmoothedRpm - smoothedRpm) >= burbleRPMDropThreshold;

            if (!rpmCondition) return;
            if (!loadCondition && !rpmDropCond) return;

            if (enableBurbleDiagnostics)
                Debug.Log($"VNS-Burble: rpm={clampedRpm:0} load={smoothedLoad:0.00} loadOk={loadCondition} rpmDrop={previousSmoothedRpm - smoothedRpm:0} rpmDropOk={rpmDropCond}");

            if (UnityEngine.Random.value > burbleProbability) return;

            AudioSource freeSource = GetFreeBurbleSource();
            if (freeSource == null) return;

            int clipIndex = UnityEngine.Random.Range(0, burbleSounds.Length);
            freeSource.clip = burbleSounds[clipIndex];
            freeSource.volume = burbleVolume;
            freeSource.pitch = 1f + UnityEngine.Random.Range(-burbleRandomPitchVariation, burbleRandomPitchVariation);
            freeSource.Play();
            nextBurbleTime = Time.time + UnityEngine.Random.Range(minBurbleDelay, maxBurbleDelay);
        }

        private void UpdateLugging(float deltaTime, float clampedRpm)
        {
            if (!enableEngineLugging || luggingSounds == null || luggingSounds.Length == 0)
            {
                FadeOutAllLugging(deltaTime);
                return;
            }

            bool rpmInRange = clampedRpm >= luggingMinRPMThreshold && clampedRpm <= luggingMaxRPMThreshold;
            bool loadSufficient = smoothedLoad >= luggingMinLoadThreshold;
            bool shouldLug = rpmInRange && loadSufficient;

            if (shouldLug)
            {
                if (activeLuggingIndex < 0) EnsureActiveLuggingSource();
                currentLuggingVolume = Mathf.Min(1f, currentLuggingVolume + luggingFadeInSpeed * deltaTime);
            }
            else
            {
                currentLuggingVolume = Mathf.Max(0f, currentLuggingVolume - luggingFadeOutSpeed * deltaTime);
                if (currentLuggingVolume <= 0f) activeLuggingIndex = -1;
            }

            for (int i = 0; i < luggingAudioSources.Count; i++)
            {
                AudioSource src = luggingAudioSources[i];
                if (i == activeLuggingIndex)
                {
                    src.volume = currentLuggingVolume * luggingVolume;
                    src.pitch = luggingBasePitch + currentLuggingPitchRandom;
                    if (!src.isPlaying) src.Play();
                }
                else
                {
                    src.volume = Mathf.Max(0f, src.volume - luggingFadeOutSpeed * deltaTime);
                    if (src.volume <= 0f && src.isPlaying) src.Stop();
                }
            }
        }

        private void EnsureActiveLuggingSource()
        {
            if (luggingSounds == null || luggingSounds.Length == 0) return;
            int clipIndex = UnityEngine.Random.Range(0, luggingSounds.Length);
            int sourceIndex = 0;
            float lowestVol = float.MaxValue;
            for (int i = 0; i < luggingAudioSources.Count; i++)
            {
                if (!luggingAudioSources[i].isPlaying) { sourceIndex = i; break; }
                if (luggingAudioSources[i].volume < lowestVol) { lowestVol = luggingAudioSources[i].volume; sourceIndex = i; }
            }
            luggingAudioSources[sourceIndex].clip = luggingSounds[clipIndex];
            luggingAudioSources[sourceIndex].volume = 0f;
            luggingAudioSources[sourceIndex].pitch = luggingBasePitch;
            luggingAudioSources[sourceIndex].Play();
            activeLuggingIndex = sourceIndex;
            currentLuggingPitchRandom = UnityEngine.Random.Range(-luggingRandomPitchVariation, luggingRandomPitchVariation);
        }

        private void FadeOutAllLugging(float deltaTime)
        {
            if (luggingAudioSources == null) return;
            for (int i = 0; i < luggingAudioSources.Count; i++)
            {
                AudioSource src = luggingAudioSources[i];
                src.volume = Mathf.Max(0f, src.volume - luggingFadeOutSpeed * deltaTime);
                if (src.volume <= 0f && src.isPlaying) src.Stop();
            }
            activeLuggingIndex = -1;
            currentLuggingVolume = 0f;
        }

        private AudioSource GetFreeBurbleSource()
        {
            for (int i = 0; i < burbleAudioSources.Count; i++)
                if (!burbleAudioSources[i].isPlaying) return burbleAudioSources[i];
            AudioSource quietest = burbleAudioSources[0];
            for (int i = 1; i < burbleAudioSources.Count; i++)
                if (burbleAudioSources[i].volume < quietest.volume) quietest = burbleAudioSources[i];
            return quietest;
        }

        private void FadeAllToSilence(float deltaTime)
        {
            float volumeSlew = EvaluateSmoothingAlpha(clipVolumeResponseTime, deltaTime);
            for (int i = 0; i < accLayers.Count; i++)
                accLayers[i].source.volume = Mathf.Lerp(accLayers[i].source.volume, 0f, volumeSlew);
            for (int i = 0; i < decLayers.Count; i++)
                decLayers[i].source.volume = Mathf.Lerp(decLayers[i].source.volume, 0f, volumeSlew);
            FadeOutAllLugging(deltaTime);
        }

        private void UpdateDiagnostics(float deltaTime)
        {
            if (!enableDiagnosticLogger) return;
            diagnosticTimer += deltaTime;
            if (diagnosticTimer < 1f) return;
            diagnosticTimer = 0f;
            var sb = new StringBuilder();
            sb.AppendLine($"VNS {name}: RPM={smoothedRpm:0} Load={smoothedLoad:0.00} Pitch={finalPitch:0.000}");
            sb.AppendLine($"  AccVol={finalAccVol:0.000} DecVol={finalDecVol:0.000}");
            AppendBankDiagnostics(sb, "ACC", accLayers, accTargetVolumes, accTargetPitches);
            AppendBankDiagnostics(sb, "DEC", decLayers, decTargetVolumes, decTargetPitches);
            Debug.Log(sb.ToString(), this);
        }

        private void AppendBankDiagnostics(StringBuilder sb, string label, List<RuntimeLayer> bank, float[] vols, float[] pitches)
        {
            if (bank == null || bank.Count == 0) return;
            sb.AppendLine($"  {label}: {bank.Count} layers");
            for (int i = 0; i < bank.Count; i++)
            {
                float tv = i < vols.Length ? vols[i] : 0f;
                float tp = i < pitches.Length ? pitches[i] : 1f;
                sb.AppendLine($"    [{i}] refRpm={bank[i].referenceRpm:0} srcVol={bank[i].source.volume:0.000} tgtVol={tv:0.000} srcPitch={bank[i].source.pitch:0.000} tgtPitch={tp:0.000}");
            }
        }

        private void DestroyAllRuntimeSources()
        {
            DestroyBank(accLayers);
            DestroyBank(decLayers);
            DestroyPool(burbleAudioSources);
            if (luggingAudioSources != null)
            {
                for (int i = 0; i < luggingAudioSources.Count; i++)
                    if (luggingAudioSources[i] != null) SafeDestroy(luggingAudioSources[i].gameObject);
                luggingAudioSources.Clear();
            }
        }

        private void DestroyBank(List<RuntimeLayer> bank)
        {
            if (bank == null) return;
            for (int i = 0; i < bank.Count; i++)
                if (bank[i]?.host != null) SafeDestroy(bank[i].host);
            bank.Clear();
        }

        private void DestroyPool(List<AudioSource> pool)
        {
            if (pool == null) return;
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null) SafeDestroy(pool[i].gameObject);
            pool.Clear();
        }

        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private float InferIdleRpm()
        {
            float lowestKnownRpm = float.MaxValue;
            if (acceleratingSounds != null)
                for (int i = 0; i < acceleratingSounds.Count; i++)
                    if (acceleratingSounds[i] != null && acceleratingSounds[i].audioClip != null)
                        lowestKnownRpm = Mathf.Min(lowestKnownRpm, acceleratingSounds[i].rpmValue);
            if (deceleratingSounds != null)
                for (int i = 0; i < deceleratingSounds.Count; i++)
                    if (deceleratingSounds[i] != null && deceleratingSounds[i].audioClip != null)
                        lowestKnownRpm = Mathf.Min(lowestKnownRpm, deceleratingSounds[i].rpmValue);
            return lowestKnownRpm < float.MaxValue ? Mathf.Max(0f, lowestKnownRpm - rpmdeviation) : 800f;
        }

        private float ComputeCombustionEventsPerRevolution()
        {
            return combustionCycleMode == CombustionCycleMode.FourStroke
                ? cylinderCount / 2f
                : cylinderCount;
        }

        private void ApplyTemplateToAudioSource(AudioSource source)
        {
            if (audioSourceTemplate != null)
            {
                source.bypassEffects = audioSourceTemplate.bypassEffects;
                source.bypassListenerEffects = audioSourceTemplate.bypassListenerEffects;
                source.bypassReverbZones = audioSourceTemplate.bypassReverbZones;
                source.priority = audioSourceTemplate.priority;
                source.volume = audioSourceTemplate.volume;
                source.pitch = audioSourceTemplate.pitch;
                source.panStereo = audioSourceTemplate.panStereo;
                source.spatialBlend = audioSourceTemplate.spatialBlend;
                source.reverbZoneMix = audioSourceTemplate.reverbZoneMix;
                source.dopplerLevel = audioSourceTemplate.dopplerLevel;
                source.spread = audioSourceTemplate.spread;
                source.rolloffMode = audioSourceTemplate.rolloffMode;
                source.minDistance = audioSourceTemplate.minDistance;
                source.maxDistance = audioSourceTemplate.maxDistance;
                source.ignoreListenerVolume = audioSourceTemplate.ignoreListenerVolume;
                source.ignoreListenerPause = audioSourceTemplate.ignoreListenerPause;
                source.mute = audioSourceTemplate.mute;
                source.outputAudioMixerGroup = mixer != null ? mixer : audioSourceTemplate.outputAudioMixerGroup;
            }
            else
            {
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 1f;
                source.maxDistance = 500f;
                source.outputAudioMixerGroup = mixer;
            }
        }
    }


}
