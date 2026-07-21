using System;
using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// Real-time multi-clip engine audio granulator (VNS v1.9).
    /// Desktop builds batch all instances in a Burst job each fixed step;
    /// WebGL builds run the same algorithm on the main thread.
    /// </summary>
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


            [Tooltip("Optional description for this audio clip.")]
            public string description;

            [Tooltip("Per-clip volume trim.")]
            [Range(-1.0f, 1.0f)]
            public float volumeOffset = 0f;

            [Tooltip("Per-clip pitch trim added to the RPM-tracked ratio.")]
            [Range(-0.5f, 0.5f)]
            public float pitchOffset = 0f;

            [Tooltip("Low-end pitch multiplier applied to this clip at minimum RPM (1 = neutral).")]
            [Range(0.01f, 3f)]
            public float loPitch = 1f;

            [Tooltip("High-end pitch multiplier applied to this clip at maximum RPM (1 = neutral).")]
            [Range(0.01f, 3f)]
            public float hiPitch = 1f;
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
#if !UNITY_WEBGL
            _batchCurvesDirty = true;
#endif
        }

#if !UNITY_WEBGL
        /// <summary>True when baked curves need re-upload into the batch NativeArrays.</summary>
        private bool _batchCurvesDirty = true;
#endif

#if !UNITY_WEBGL
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
            public float loadEffectivenessOnPitch;
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
            /// <summary>How far RPM may leave the active pair window before hold escape / pitch clamp (e.g. 1.2 = ±20%).</summary>
            public float maxPitchRatioBeyondPair;
            public float currentTime;
            public float fixedDeltaTime;
        }

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

                // Global pitch contour; per-clip RPM ratio is applied in EvalPitchInJob.
                float curvePitch = SampleJob(BakedPitchCurves, curveBase, inp.normalizedRpm);
                float loadPitchContrib = inp.smoothedLoad * inp.loadEffectivenessOnPitch;
                o.finalPitch = MathMax(0.01f,
                    curvePitch + loadPitchContrib + inp.shiftPitchOsc);

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

                float lpCurveValue = ClampF(
                    SampleJob(BakedLowPassCurves, curveBase, inp.smoothedLoad), 500f, 22000f);
                float lpMix = Clamp01(MathMax(inp.lowPassIntensity,
                    inp.lowPassStrength + inp.mufflingIntensity));
                o.lowPassTarget = Lerp(22000f, lpCurveValue, lpMix);

                float hpAmount = Clamp01(inp.normalizedRpm * inp.normalizedRpm * inp.highPassStrength);
                o.highPassTarget = Lerp(10f, 1800f, hpAmount);

                float resShape = math.sin(inp.normalizedRpm * math.PI);
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

            private float SampleJob(NativeArray<float> table, int baseIndex, float t)
            {
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                int n = CurveSamples > 1 ? CurveSamples : 256;
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
            private static int MathAbs(int v) => v < 0 ? -v : v;
            private static float Lerp(float a, float b, float t) => a + (b - a) * t;
            private static float InverseLerp(float a, float b, float t) =>
                (b - a) < 0.00001f ? 0f : Clamp01((t - a) / (b - a));
            private static float SmoothStep(float t)
            {
                t = Clamp01(t);
                return t * t * (3f - 2f * t);
            }

            /// <summary>Converts pairHoldCycles into a duration rounded up to whole fixedDeltaTime ticks (0 if RPM/combustion is 0).</summary>
            private static float QuantizeHoldToTicks(
                float pairHoldCycles, float smoothedRpm,
                float combustionEventsPerRev, float fixedDeltaTime)
            {
                float ft = fixedDeltaTime > 0.00001f ? fixedDeltaTime : 0.02f;
                float rawHold = (combustionEventsPerRev > 0f && smoothedRpm > 0f)
                    ? pairHoldCycles / (smoothedRpm / 60f * combustionEventsPerRev)
                    : 0f;
                float holdTicks = math.ceil(rawHold / ft);
                return holdTicks * ft;
            }

            private static void EvaluateBankInJob(
                VehicleCalcInput inp, float finalPitch, float bankFinalVol,
                NativeArray<LayerData> layerData, int layerOffset, int count,
                bool isAcc, float bankVolumeLimit, float bankPitchTrim,
                bool stateInitialized,
                int stateLowIndex,
                int stateHighIndex,
                float stateHoldUntilTime,
                out int outLowIndex, out int outHighIndex,
                out float outLowVolume, out float outHighVolume,
                out float outLowPitch, out float outHighPitch,
                out bool outStateInitialized,
                out int outStateLow,
                out int outStateHigh,
                out float outStateHoldUntil)
            {
                outLowIndex = 0; outHighIndex = 0;
                outLowVolume = 0f; outHighVolume = 0f;
                outLowPitch = 1f; outHighPitch = 1f;

                outStateInitialized = stateInitialized;
                outStateLow = stateLowIndex;
                outStateHigh = stateHighIndex;
                outStateHoldUntil = stateHoldUntilTime;

                if (count <= 0) return;

                float loadGainRaw = isAcc
                    ? Lerp(inp.loadVolumeChangerMinValue, 1f, inp.smoothedLoad)
                    : Lerp(inp.loadVolumeChangerMinValue, 1f, 1f - inp.smoothedLoad);
                float loadGain = isAcc
                    ? Lerp(1f, loadGainRaw, inp.loadVolumeAccChangerFactor)
                    : Lerp(1f, loadGainRaw, inp.loadVolumeDccChangerFactor);
                float bankBaseGain = inp.masterVolume * bankFinalVol * MathMax(0f, loadGain);

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

                int lo, hi;
                int curLo = stateLowIndex < count ? stateLowIndex : count - 1;
                int curHi = stateHighIndex < count ? stateHighIndex : count - 1;
                float stretch = MathMax(1f, inp.maxPitchRatioBeyondPair);

                if (!stateInitialized)
                {
                    lo = idealLo; hi = idealHi;
                    outStateInitialized = true;
                    outStateLow = lo;
                    outStateHigh = hi;
                    float holdDur = QuantizeHoldToTicks(
                        inp.pairHoldCycles, inp.smoothedRpm,
                        inp.combustionEventsPerRev, inp.fixedDeltaTime);
                    outStateHoldUntil = inp.currentTime + holdDur;
                }
                else
                {
                    bool inHold = inp.currentTime < stateHoldUntilTime;
                    bool wantsSwitch = (idealLo != curLo || idealHi != curHi);
                    int stepGap = MathAbs(idealHi - curHi);
                    if (MathAbs(idealLo - curLo) > stepGap) stepGap = MathAbs(idealLo - curLo);
                    bool multiStepBehind = stepGap > 1;

                    // Leave hold early if RPM is far outside the pair window, or multi-step behind.
                    bool stretchEscape = false;
                    if (count > 1 && (inHold || wantsSwitch))
                    {
                        float pairMin = layerData[layerOffset + curLo].referenceRpm;
                        float pairMax = layerData[layerOffset + curHi].referenceRpm;
                        if (pairMin > pairMax) { float tmp = pairMin; pairMin = pairMax; pairMax = tmp; }
                        stretchEscape =
                            inp.clampedRpm > pairMax * stretch ||
                            inp.clampedRpm < pairMin / stretch;
                    }

                    bool forceIdeal = multiStepBehind || stretchEscape;

                    if (inHold && !forceIdeal)
                    {
                        lo = curLo;
                        hi = curHi;
                    }
                    else
                    {
                        bool passesHysteresis = false;
                        if (wantsSwitch && count > 1)
                        {
                            if (idealHi > curHi)
                            {
                                float boundary = layerData[layerOffset + curHi].referenceRpm;
                                passesHysteresis = inp.clampedRpm > boundary + inp.pairHysteresisRpm;
                            }
                            else if (idealHi < curHi)
                            {
                                float boundary = layerData[layerOffset + curLo].referenceRpm;
                                passesHysteresis = inp.clampedRpm < boundary - inp.pairHysteresisRpm;
                            }
                            else
                            {
                                passesHysteresis = true; // same hi, different lo
                            }
                        }

                        if (wantsSwitch && (passesHysteresis || forceIdeal))
                        {
                            lo = idealLo;
                            hi = idealHi;
                            outStateLow = lo;
                            outStateHigh = hi;
                            float holdDur = QuantizeHoldToTicks(
                                inp.pairHoldCycles, inp.smoothedRpm,
                                inp.combustionEventsPerRev, inp.fixedDeltaTime);
                            outStateHoldUntil = inp.currentTime + holdDur;
                        }
                        else
                        {
                            lo = curLo;
                            hi = curHi;
                        }
                    }
                }

                outLowIndex = lo; outHighIndex = hi;

                float pairLoRef = layerData[layerOffset + lo].referenceRpm;
                float pairHiRef = layerData[layerOffset + hi].referenceRpm;
                float pitchRpm = ClampRpmForPairPitch(inp.clampedRpm, pairLoRef, pairHiRef, stretch);

                float t = 0f;
                if (lo != hi)
                {
                    float a = pairLoRef;
                    float b = pairHiRef;
                    t = InverseLerp(a, b, inp.clampedRpm);
                }

                if (lo == hi)
                {
                    LayerData ld = layerData[layerOffset + lo];
                    float g = bankBaseGain * MathMax(0f, 1f + ld.volumeOffset);
                    if (bankVolumeLimit > 0f) g = g > bankVolumeLimit ? bankVolumeLimit : g;
                    outLowVolume = outHighVolume = g;
                    outLowPitch = outHighPitch =
                        EvalPitchInJob(ld, pitchRpm, finalPitch, bankPitchTrim, inp.maxRpm, inp.idleRpm);
                    return;
                }

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
                outLowPitch = EvalPitchInJob(ldLo, pitchRpm, finalPitch, bankPitchTrim, inp.maxRpm, inp.idleRpm);
                outHighPitch = EvalPitchInJob(ldHi, pitchRpm, finalPitch, bankPitchTrim, inp.maxRpm, inp.idleRpm);
            }

            /// <summary>Soft-clamps RPM used for pitch into the active pair window (± stretch).</summary>
            private static float ClampRpmForPairPitch(
                float clampedRpm, float pairLoRef, float pairHiRef, float stretch)
            {
                float minRef = pairLoRef < pairHiRef ? pairLoRef : pairHiRef;
                float maxRef = pairLoRef > pairHiRef ? pairLoRef : pairHiRef;
                minRef = MathMax(1f, minRef);
                maxRef = MathMax(minRef, maxRef);
                float s = stretch > 1f ? stretch : 1f;
                float lo = minRef / s;
                float hi = maxRef * s;
                return ClampF(clampedRpm, lo, hi);
            }

            private static float EvalPitchInJob(
                LayerData ld, float pitchRpm, float finalPitch,
                float bankPitchTrim, float maxRpm, float idleRpm)
            {
                float referenceRpm = MathMax(1f, ld.referenceRpm);
                float ratioPitch = (pitchRpm / referenceRpm) * finalPitch;
                // loPitch/hiPitch progress uses idle as 0 (same basis as normalizedRpm).
                float progress = Clamp01((pitchRpm - idleRpm) / MathMax(1f, maxRpm - idleRpm));
                float hiLoMul = Lerp(ld.minPitch, ld.maxPitch, progress);

                float pitch = ratioPitch * hiLoMul + bankPitchTrim + ld.pitchOffset;
                // AudioSource.pitch for clips is limited to about [-3, 3]; keep forward play in (0, 3].
                return ClampF(pitch, 0.01f, 3f);
            }

        }

        /// <summary>
        /// Shared Burst batch for all active synthesizers (non-WebGL only).
        /// One <see cref="IJobParallelFor"/> per fixed step; results applied the same tick
        /// so volume/pitch slew can run immediately. Compiled out on WebGL.
        /// </summary>
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
            private static double sLastDispatchFixedTime = -1d;
            private static JobHandle sPendingHandle;
            private static bool sJobPending;

            public static void Register(VehicleNoiseSynthesizer instance)
            {
                if (!sInstances.Contains(instance))
                    sInstances.Add(instance);
                sNeedsRebuild = true;
                instance._batchCurvesDirty = true;
            }

            public static void Unregister(VehicleNoiseSynthesizer instance)
            {
                CompletePendingJob(applyResults: false);
                if (!sInstances.Remove(instance)) return;
                // Free Persistent buffers when nothing remains (no later tick will run).
                if (sInstances.Count == 0) DisposeNativeArrays();
                else sNeedsRebuild = true;
            }

            /// <summary>Mark layer/curve NativeArrays dirty after audio banks are rebuilt.</summary>
            public static void NotifyLayoutChanged(VehicleNoiseSynthesizer instance)
            {
                if (instance == null) return;
                if (!sInstances.Contains(instance)) return;
                sNeedsRebuild = true;
                instance._batchCurvesDirty = true;
            }

            /// <summary>
            /// First active synthesizer each fixed step schedules the batch; others no-op here.
            /// Always Completes before returning so ApplySlewAndEffects can use fresh targets.
            /// </summary>
            public static void ExecuteBatchIfNeeded(float deltaTime)
            {
                int count = sInstances.Count;
                if (sNeedsRebuild || count != sAllocatedCount)
                {
                    CompletePendingJob(applyResults: false);
                    if (count > 0) RebuildNativeArrays(count);
                    else
                    {
                        DisposeNativeArrays();
                        return;
                    }
                    // Fall through: schedule on the same tick after rebuild (avoids a silent first step).
                }

                if (count == 0) return;
                if (!ShouldDispatchThisFixedTick()) return;

                CompletePendingJob(applyResults: true);

                for (int v = 0; v < count; v++)
                {
                    VehicleNoiseSynthesizer syn = sInstances[v];
                    sInputs[v] = syn.PackJobInput();

                    if (syn._batchCurvesDirty)
                    {
                        UploadCurvesForInstance(v, syn);
                        syn._batchCurvesDirty = false;
                    }
                }

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

                // batchSize 1 for parallel vehicles; one batch for 1–3 instances (less split overhead).
                int batchSize = count <= 3 ? count : 1;
                if (batchSize < 1) batchSize = 1;

                sPendingHandle = job.Schedule(count, batchSize);
                sJobPending = true;
                CompletePendingJob(applyResults: true);
            }

            private static void CompletePendingJob(bool applyResults)
            {
                if (!sJobPending) return;
                sPendingHandle.Complete();
                sJobPending = false;

                if (!applyResults || !sOutputs.IsCreated) return;

                int count = sInstances.Count;
                int n = count < sAllocatedCount ? count : sAllocatedCount;
                for (int v = 0; v < n; v++)
                    sInstances[v].ApplyJobData(sOutputs[v]);
            }

            private static void UploadCurvesForInstance(int vehicleIndex, VehicleNoiseSynthesizer syn)
            {
                if (syn.bakedPitchCurve == null || syn.bakedVolumeCurve == null ||
                    syn.bakedDistortionCurve == null || syn.bakedLowPassCurve == null)
                    return;

                int curveBase = vehicleIndex * CurveBakeResolution;
                for (int i = 0; i < CurveBakeResolution; i++)
                {
                    sBakedPitch[curveBase + i] = syn.bakedPitchCurve[i];
                    sBakedVolume[curveBase + i] = syn.bakedVolumeCurve[i];
                    sBakedDistortion[curveBase + i] = syn.bakedDistortionCurve[i];
                    sBakedLowPass[curveBase + i] = syn.bakedLowPassCurve[i];
                }
            }

            private static void UploadLayerDataForInstance(int vehicleIndex, VehicleNoiseSynthesizer syn)
            {
                int accOff = sAccOffsets[vehicleIndex];
                for (int i = 0; i < syn.accLayers.Count; i++)
                {
                    RuntimeLayer rl = syn.accLayers[i];
                    sAccLayers[accOff + i] = new LayerData
                    {
                        referenceRpm = rl.referenceRpm,
                        minPitch = rl.minPitch,
                        maxPitch = rl.maxPitch,
                        volumeOffset = rl.volumeOffset,
                        pitchOffset = rl.pitchOffset
                    };
                }

                int decOff = sDecOffsets[vehicleIndex];
                for (int i = 0; i < syn.decLayers.Count; i++)
                {
                    RuntimeLayer rl = syn.decLayers[i];
                    sDecLayers[decOff + i] = new LayerData
                    {
                        referenceRpm = rl.referenceRpm,
                        minPitch = rl.minPitch,
                        maxPitch = rl.maxPitch,
                        volumeOffset = rl.volumeOffset,
                        pitchOffset = rl.pitchOffset
                    };
                }
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
                    VehicleNoiseSynthesizer syn = sInstances[v];
                    UploadLayerDataForInstance(v, syn);
                    UploadCurvesForInstance(v, syn);
                    syn._batchCurvesDirty = false;
                }

                sAllocatedCount = count;
                sNeedsRebuild = false;
            }

            private static void DisposeNativeArrays()
            {
                if (sJobPending)
                {
                    sPendingHandle.Complete();
                    sJobPending = false;
                }

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

            /// <summary>True once per fixed step (shared gate for all instances).</summary>
            public static bool ShouldDispatchThisFixedTick()
            {
                double t = Time.fixedTimeAsDouble;
                if (sLastDispatchFixedTime == t) return false;
                sLastDispatchFixedTime = t;
                return true;
            }
        }

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
                loadEffectivenessOnPitch = loadEffectivenessOnPitch,
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
                maxPitchRatioBeyondPair = maxPitchRatioBeyondPair,
                currentTime = Time.time,
                fixedDeltaTime = Time.fixedDeltaTime
            };
        }

        private void ApplyJobData(VehicleCalcOutput o)
        {
            finalPitch = o.finalPitch;
            finalAccVol = o.finalAccVol;
            finalDecVol = o.finalDecVol;

            if (enablePairSelectorDiagnostics)
            {
                float ft = Time.fixedDeltaTime;
                float cevpr = combustionEventsPerRev;
                float rawHold = (cevpr > 0f && smoothedRpm > 0f)
                    ? pairHoldCycles / (smoothedRpm / 60f * cevpr)
                    : 0f;
                float effHold = Mathf.Ceil(rawHold / Mathf.Max(0.00001f, ft)) * ft;
                int holdTicks = Mathf.RoundToInt(effHold / Mathf.Max(0.00001f, ft));

                bool accSwitched = accBlendState.initialized &&
                    (o.accStateLowIndex != accBlendState.lowIndex || o.accStateHighIndex != accBlendState.highIndex);
                if (accSwitched) _pairDiagAccSwitches++;

                bool decSwitched = decBlendState.initialized &&
                    (o.decStateLowIndex != decBlendState.lowIndex || o.decStateHighIndex != decBlendState.highIndex);
                if (decSwitched) _pairDiagDecSwitches++;

                if (Time.time >= _pairDiagLogTime + 1f)
                {
                    _pairDiagLogTime = Time.time;
                    float clampedRpm = Mathf.Clamp(smoothedRpm, 0f, Mathf.Max(1f, maxRpm));
                    Debug.Log(
                        $"[VNS PairDiag | {name}] RPM={clampedRpm:0} " +
                        $"combustionEventsPerRev={cevpr:0.0} " +
                        $"rawHold={rawHold * 1000f:0.00}ms effHold={effHold * 1000f:0.00}ms ({holdTicks} ticks) " +
                        $"fixedDt={ft * 1000f:0.00}ms " +
                        $"pairHysteresisRpm={pairHysteresisRpm:0}\n" +
                        $"  ACC: pair=[{o.accStateLowIndex},{o.accStateHighIndex}] switches/s={_pairDiagAccSwitches}\n" +
                        $"  DEC: pair=[{o.decStateLowIndex},{o.decStateHighIndex}] switches/s={_pairDiagDecSwitches}",
                        this);
                    _pairDiagAccSwitches = 0;
                    _pairDiagDecSwitches = 0;
                }
            }

            accBlendState.initialized = o.accStateInitialized;
            accBlendState.lowIndex = o.accStateLowIndex;
            accBlendState.highIndex = o.accStateHighIndex;
            accBlendState.holdUntilTime = o.accStateHoldUntilTime;
            decBlendState.initialized = o.decStateInitialized;
            decBlendState.lowIndex = o.decStateLowIndex;
            decBlendState.highIndex = o.decStateHighIndex;
            decBlendState.holdUntilTime = o.decStateHoldUntilTime;

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

                // Skip disabled filter components (no need to write idle DSP params).
                if (layer.lowPass != null && layer.lowPass.enabled)
                {
                    layer.lowPass.cutoffFrequency =
                        Mathf.MoveTowards(layer.lowPass.cutoffFrequency, o.lowPassTarget, slew2000);
                    layer.lowPass.lowpassResonanceQ =
                        Mathf.MoveTowards(layer.lowPass.lowpassResonanceQ, o.lowPassQTarget, slewBase);
                }
                if (layer.highPass != null && layer.highPass.enabled)
                {
                    layer.highPass.cutoffFrequency =
                        Mathf.MoveTowards(layer.highPass.cutoffFrequency, o.highPassTarget, slew180);
                    layer.highPass.highpassResonanceQ =
                        Mathf.MoveTowards(layer.highPass.highpassResonanceQ, o.highPassQTarget, slewBase);
                }
                if (layer.distortion != null && layer.distortion.enabled)
                {
                    layer.distortion.distortionLevel =
                        Mathf.MoveTowards(layer.distortion.distortionLevel, o.distortionTarget, slewBase);
                }
                if (layer.chorus != null && layer.chorus.enabled)
                {
                    layer.chorus.dryMix = Mathf.MoveTowards(layer.chorus.dryMix, o.chorusDryTarget, slewBase);
                    layer.chorus.wetMix1 = Mathf.MoveTowards(layer.chorus.wetMix1, o.chorusWet * 0.6f, slewBase);
                    layer.chorus.wetMix2 = Mathf.MoveTowards(layer.chorus.wetMix2, o.chorusWet * 0.3f, slewBase);
                    layer.chorus.wetMix3 = Mathf.MoveTowards(layer.chorus.wetMix3, o.chorusWet * 0.1f, slewBase);
                    layer.chorus.depth = Mathf.MoveTowards(layer.chorus.depth, o.chorusDepth, slewBase);
                    layer.chorus.rate = Mathf.MoveTowards(layer.chorus.rate, o.chorusRate, slewBase);
                }
                if (layer.reverb != null && layer.reverb.enabled)
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

            float slew2000 = filterLPFSlewHz * deltaTime;
            float slew180 = filterHPFSlewHz * deltaTime;
            float slew2500 = filterReverbSlewDbS * deltaTime;
            float slewBase = filterParamSlewRate * deltaTime;

            ApplyPrecomputedFilters(accLayers, accTargetVolumes, _lastJobOutput,
                slewBase, slew2000, slew180, slew2500);
            ApplyPrecomputedFilters(decLayers, decTargetVolumes, _lastJobOutput,
                slewBase, slew2000, slew180, slew2500);

            if (useSharedMixerReverb && mixer != null && !string.IsNullOrEmpty(reverbMixerParamName))
                mixer.audioMixer.SetFloat(reverbMixerParamName, _lastJobOutput.reverbAmountDb);

            float clampedRpm = Mathf.Clamp(smoothedRpm, 0f, Mathf.Max(1f, maxRpm));
            float normalizedRpmForFx = Mathf.InverseLerp(
                Mathf.Max(0f, idleRpm), Mathf.Max(idleRpm + 1f, maxRpm), clampedRpm);
            UpdateBurble(deltaTime, clampedRpm);
            UpdateRedlineEffect(clampedRpm);
            ApplyPostEffectsToDctShiftSource(normalizedRpmForFx, deltaTime);
            UpdateDiagnostics(deltaTime);
        }

#endif

        [Header("Debug Controls")]
        [Space(5)]
        [Tooltip("Enable to manually control RPM and load values for testing.")]
        public bool debug = false;

        [Tooltip("Enable pair-selector diagnostic logger. Prints hold-duration vs physics-tick, hysteresis state, and switch events once per second.")]
        public bool enablePairSelectorDiagnostics = false;

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
        public AnimationCurve pitchCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));

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

        [Range(0f, 20f)]
        [Tooltip("Minimum hold after a pair switch, in combustion-event cycles. The resulting duration is rounded UP to whole physics ticks (fixedDeltaTime), so it actually blocks switching. 0 disables the hold. Raise this for longer, more obvious stability. High values (e.g. 16) cause long pitch stretch if RPM races ahead of the held pair.")]
        public float pairHoldCycles = 0.5f;

        [Range(1f, 2f)]
        [Tooltip("How far live RPM may go past the active pair's outer reference RPM before pitch is soft-clamped and the pair hold is escaped (1.2 = ±20%). Prevents chipmunk pitch during pair catch-up.")]
        public float maxPitchRatioBeyondPair = 1.2f;

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
        [Tooltip("Minimum engine load required for burble to be allowed (inclusive). Burble is suppressed below this value, e.g. full coasting.")]
        public float burbleLoadLowThreshold = 0.0f;

        [Range(0f, 1f)]
        [Tooltip("Maximum engine load at which burble is allowed (exclusive). Burble is suppressed above this value, e.g. under power.")]
        public float burbleLoadHighThreshold = 0.3f;

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

        [Header("DCT Shift Burble Configuration")]
        [Space(10)]
        [Tooltip("Enable the looping DCT shift exhaust overlay.")]
        public bool enableDctShiftBurble = true;

        [Tooltip("Looped exhaust clip used for the DCT-style shift overlay.")]
        public AudioClip dctShiftBurbleSound;

        [Range(0f, 1f)]
        [Tooltip("Master volume for the DCT shift burble overlay.")]
        public float dctShiftBurbleVolume = 0.7f;

        [Range(0f, 1f)]
        [Tooltip("How much RPM scales the DCT shift volume. At 0 volume is constant; at 1 volume rises linearly from zero at the min-RPM threshold to full at max RPM.")]
        public float dctShiftBurbleRpmVolumeInfluence = 0.5f;

        [Tooltip("Minimum RPM for the RPM-volume scaling range. Volume is zero below this point when influence > 0.")]
        public float dctShiftBurbleMinRPM = 2000f;

        [Range(0.01f, 1f)]
        [Tooltip("Maximum time the DCT shift burble overlay may play for a single trigger.")]
        public float dctShiftBurbleMaxDuration = 0.12f;

        [Range(0.5f, 2f)]
        [Tooltip("Base pitch for the DCT shift burble overlay.")]
        public float dctShiftBurbleBasePitch = 1f;

        [Range(0f, 0.3f)]
        [Tooltip("Random ±pitch variation for the DCT shift burble overlay.")]
        public float dctShiftBurblePitchVariation = 0.05f;

        [Header("Throttle Body Configuration")]
        [Space(10)]
        [Tooltip("Enable throttle body sounds: intake roar on tip-in, flutter on tip-out.")]
        public bool enableThrottleBody = true;

        [Tooltip("Audio clips played as a one-shot when the throttle snaps open (tip-in / intake roar).")]
        public AudioClip[] intakeRoarSounds;

        [Tooltip("Audio clips played as a one-shot when the throttle snaps shut at high RPM (tip-out / flutter).")]
        public AudioClip[] throttleFlutterSounds;

        [Range(0f, 1f)]
        [Tooltip("Master volume for intake roar clips. Final volume is scaled linearly by normalised RPM.")]
        public float intakeRoarVolume = 0.6f;

        [Range(0f, 1f)]
        [Tooltip("Master volume for throttle flutter clips. Final volume is scaled linearly by normalised RPM.")]
        public float throttleFlutterVolume = 0.5f;

        [Range(0f, 0.3f)]
        [Tooltip("Random ±pitch variation applied to each throttle body one-shot.")]
        public float throttleBodyPitchVariation = 0.05f;

        [Range(0.01f, 1f)]
        [Tooltip("Minimum seconds between successive throttle body triggers (prevents spamming).")]
        public float throttleBodyCooldown = 0.08f;

        [Header("Exhaust Redline Configuration")]
        [Space(10)]
        [Tooltip("Enable the exhaust crackle/pressure-wave sound that repeats while the engine is held near redline.")]
        public bool enableRedlineEffect = true;

        [Tooltip("Audio clips played in a repeating loop while RPM stays within the redline range.")]
        public AudioClip[] redlineSounds;

        [Range(0f, 1f)]
        [Tooltip("Master volume for redline exhaust clips.")]
        public float redlineVolume = 0.6f;

        [Tooltip("RPM at which the redline effect begins to trigger.")]
        public float redlineMinRPM = 7000f;

        [Tooltip("RPM ceiling for the redline effect (0 = no upper limit, uses maxRpm).")]
        public float redlineMaxRPM = 0f;

        [Range(0.01f, 1f)]
        [Tooltip("Minimum delay (seconds) between successive redline one-shot clips.")]
        public float redlineMinDelay = 0.05f;

        [Range(0.01f, 2f)]
        [Tooltip("Maximum delay (seconds) between successive redline one-shot clips.")]
        public float redlineMaxDelay = 0.2f;

        [Range(0.5f, 2f)]
        [Tooltip("Base pitch for redline clips.")]
        public float redlineBasePitch = 1f;

        [Range(0f, 0.3f)]
        [Tooltip("Random ±pitch variation per redline clip.")]
        public float redlinePitchVariation = 0.05f;

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

        // [Range(0.5f, 2f)]
        // [Tooltip("Base pitch value when engine is near idle.")]
        // public float idlePitch = 1f;

        /// <summary>Forces full acceleration-bank volume (internal/API). Not shown in the inspector.</summary>
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
        private const int BURBLEAUDIOPOOLSIZE = 5;
        [Header("Filter Slew Rates")]
        [Range(100f, 5000f)]
        [Tooltip("Max Hz/tick the low-pass cutoff may move per fixed update.")]
        public float filterLPFSlewHz = 800f;

        [Range(10f, 500f)]
        [Tooltip("Max Hz/tick the high-pass cutoff may move per fixed update.")]
        public float filterHPFSlewHz = 180f;

        [Range(500f, 5000f)]
        [Tooltip("Max dB/tick the reverb level may move per fixed update.")]
        public float filterReverbSlewDbS = 2500f;

        [Range(0.1f, 5f)]
        [Tooltip("Slew rate multiplier for 0-1 range parameters (Q, distortion, chorus).")]
        public float filterParamSlewRate = 1f;
        private readonly List<RuntimeLayer> accLayers = new List<RuntimeLayer>();
        private readonly List<RuntimeLayer> decLayers = new List<RuntimeLayer>();
        private readonly List<AudioSource> burbleAudioSources = new List<AudioSource>();
        private float[] accTargetVolumes = Array.Empty<float>();
        private float[] accTargetPitches = Array.Empty<float>();
        private float[] decTargetVolumes = Array.Empty<float>();
        private float[] decTargetPitches = Array.Empty<float>();
        private readonly List<AudioSource> intakeRoarAudioSources = new List<AudioSource>();
        private readonly List<AudioSource> throttleFlutterAudioSources = new List<AudioSource>();
        private float nextThrottleBodyTime;
        private const int THROTTLEBODYAUDIOPOOLSIZE = 3;
        private readonly List<AudioSource> redlineAudioSources = new List<AudioSource>();
        private float nextRedlineTime;
        private const int REDLINEAUDIOPOOLSIZE = 3;
        private Coroutine calculationRoutine;
        private float smoothedRpm;
        private float smoothedLoad;
        private float previousSmoothedRpm;
        private float previousSmoothedLoad;

        private float nextBurbleTime;
        private AudioSource dctShiftBurbleSource;
        private AudioLowPassFilter dctShiftBurbleLowPass;
        private AudioHighPassFilter dctShiftBurbleHighPass;
        private AudioDistortionFilter dctShiftBurbleDistortion;
        private AudioChorusFilter dctShiftBurbleChorus;
        private float dctShiftBurbleStopTime;
        private float dctShiftBurbleTargetVolume;
        private float dctShiftBurbleFadeStartVolume;
        private float dctShiftBurbleFadeStartTime;
        private float dctShiftBurbleFadeEndTime;
        private enum DctShiftFadeState { Idle, FadeIn, Sustain, FadeOut }
        private DctShiftFadeState dctShiftFadeState = DctShiftFadeState.Idle;
        private const float DctShiftFadeDuration = 0.020f; // 20 ms
        private float diagnosticTimer;
        private BankBlendState accBlendState;
        private BankBlendState decBlendState;
        // Pair-selector diagnostics
        private float _pairDiagLogTime = -1f;
        private int _pairDiagAccSwitches;
        private int _pairDiagAccHystBlocked;
        private int _pairDiagAccHoldBlocked;
        private int _pairDiagDecSwitches;
        private int _pairDiagDecHystBlocked;
        private int _pairDiagDecHoldBlocked;
        public float rpm
        {
            get => _rpm;
            // Nested Mathf.Max(a,b) only — 3+ args use params float[] and allocate every call.
            set => _rpm = Mathf.Clamp(value, 0f, Mathf.Max(Mathf.Max(_maxRpm, maximumTheoricalRPM), 1f));
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

        /// <summary>Called by integration scripts when the throttle snaps open (tip-in, 0→1).</summary>
        public void OnThrottleTipIn(float throttleValue, float rpm, float engineLoad)
        {
            if (!enableThrottleBody || intakeRoarSounds == null || intakeRoarSounds.Length == 0) return;
            if (!_isOn) return;
            float effectiveCoefficient = throttleValue * Mathf.Max(0.5f, engineLoad);
            if (effectiveCoefficient <= 0.001f) return;
            if (Time.time < nextThrottleBodyTime) return;

            AudioSource src = GetFreeThrottleBodySource(intakeRoarAudioSources);
            if (src == null) return;

            float normalizedRpm = Mathf.InverseLerp(Mathf.Max(0f, _idleRpm), Mathf.Max(_idleRpm + 1f, _maxRpm), rpm);
            src.clip = intakeRoarSounds[UnityEngine.Random.Range(0, intakeRoarSounds.Length)];
            src.volume = intakeRoarVolume * effectiveCoefficient;
            src.pitch = 1f + normalizedRpm * 0.2f +
                         UnityEngine.Random.Range(-throttleBodyPitchVariation, throttleBodyPitchVariation);
            src.Play();
            nextThrottleBodyTime = Time.time + throttleBodyCooldown;
        }

        /// <summary>Called by integration scripts when the throttle snaps shut (tip-out, 1→0).</summary>
        public void OnThrottleTipOut(float rpm, float engineLoad)
        {
            if (!enableThrottleBody || throttleFlutterSounds == null || throttleFlutterSounds.Length == 0) return;
            if (!_isOn) return;
            if (Time.time < nextThrottleBodyTime) return;

            AudioSource src = GetFreeThrottleBodySource(throttleFlutterAudioSources);
            if (src == null) return;

            float normalizedRpm = Mathf.InverseLerp(Mathf.Max(0f, _idleRpm), Mathf.Max(_idleRpm + 1f, _maxRpm), rpm);
            src.clip = throttleFlutterSounds[UnityEngine.Random.Range(0, throttleFlutterSounds.Length)];
            src.volume = throttleFlutterVolume;
            src.pitch = 1f + normalizedRpm * 0.3f +
                         UnityEngine.Random.Range(-throttleBodyPitchVariation, throttleBodyPitchVariation);
            src.Play();
            nextThrottleBodyTime = Time.time + throttleBodyCooldown;
        }

        /// <summary>Called by integration scripts when a gear shift occurs.</summary>
        public void OnGearShift()
        {
            if (!enableDctShiftBurble || dctShiftBurbleSource == null || dctShiftBurbleSound == null) return;

            float normalizedRpm = Mathf.InverseLerp(
                Mathf.Max(0f, dctShiftBurbleMinRPM),
                Mathf.Max(dctShiftBurbleMinRPM + 1f, _maxRpm),
                smoothedRpm);

            float rpmVolScale = Mathf.Lerp(1f - dctShiftBurbleRpmVolumeInfluence, 1f, normalizedRpm);

            ResetDctShiftFxState();

            dctShiftBurbleStopTime = Time.time + dctShiftBurbleMaxDuration;
            dctShiftBurbleSource.clip = dctShiftBurbleSound;
            dctShiftBurbleSource.volume = 0f;
            dctShiftBurbleTargetVolume = dctShiftBurbleVolume * rpmVolScale;
            dctShiftBurbleSource.pitch = Mathf.Clamp(
                dctShiftBurbleBasePitch + UnityEngine.Random.Range(-dctShiftBurblePitchVariation, dctShiftBurblePitchVariation),
                0.01f, 3f);
            dctShiftBurbleSource.Play();
            BeginDctShiftFade(DctShiftFadeState.FadeIn, 0f);
        }

        /// <summary>Zeroes chorus/distortion/LPF so a retrigger starts from a clean state.</summary>
        private void ResetDctShiftFxState()
        {
            if (dctShiftBurbleDistortion != null)
            {
                dctShiftBurbleDistortion.distortionLevel = 0f;
                dctShiftBurbleDistortion.enabled = false;
            }
            if (dctShiftBurbleChorus != null)
            {
                dctShiftBurbleChorus.enabled = false;
                dctShiftBurbleChorus.dryMix = 1f;
                dctShiftBurbleChorus.wetMix1 = 0f;
                dctShiftBurbleChorus.wetMix2 = 0f;
                dctShiftBurbleChorus.wetMix3 = 0f;
                dctShiftBurbleChorus.depth = 0f;
            }
            if (dctShiftBurbleLowPass != null)
                dctShiftBurbleLowPass.cutoffFrequency = 22000f;
        }

        /// <summary>Begins a fade-in or fade-out envelope for the DCT shift one-shot.</summary>
        private void BeginDctShiftFade(DctShiftFadeState state, float fromVolume)
        {
            dctShiftFadeState = state;
            dctShiftBurbleFadeStartVolume = fromVolume;
            dctShiftBurbleFadeStartTime = Time.time;
            dctShiftBurbleFadeEndTime = Time.time + DctShiftFadeDuration;
        }

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
            try
            {
                cylinderCount = Mathf.Max(1, cylinderCount);
                rpmdeviation = Mathf.Max(1, rpmdeviation);
                maximumTheoricalRPM = Mathf.Max(1000f, maximumTheoricalRPM);
                burbleLoadLowThreshold = Mathf.Clamp01(burbleLoadLowThreshold);
                burbleLoadHighThreshold = Mathf.Clamp(burbleLoadHighThreshold, burbleLoadLowThreshold, 1f);
                minBurbleDelay = Mathf.Max(0.01f, minBurbleDelay);
                maxBurbleDelay = Mathf.Max(minBurbleDelay, maxBurbleDelay);
                dctShiftBurbleMaxDuration = Mathf.Max(0.01f, dctShiftBurbleMaxDuration);
                dctShiftBurbleBasePitch = Mathf.Clamp(dctShiftBurbleBasePitch, 0.5f, 2f);
                dctShiftBurblePitchVariation = Mathf.Clamp(dctShiftBurblePitchVariation, 0f, 0.3f);
                loadVolumeChangerMinValue = Mathf.Clamp(loadVolumeChangerMinValue, 0f, 0.99f);
                loadBlendWidth = Mathf.Max(0.01f, loadBlendWidth);
                clipVolumeResponseTime = Mathf.Max(0.005f, clipVolumeResponseTime);
                clipPitchResponseTime = Mathf.Max(0.005f, clipPitchResponseTime);
                maxPitchRatioBeyondPair = Mathf.Clamp(maxPitchRatioBeyondPair, 1f, 2f);
                rpmResponseTime = Mathf.Max(0.005f, rpmResponseTime);
                loadResponseTime = Mathf.Max(0.005f, loadResponseTime);
                pairHysteresisRpm = Mathf.Max(0f, pairHysteresisRpm);
                pairHoldCycles = Mathf.Max(0f, pairHoldCycles);
                combustionEventsPerRev = ComputeCombustionEventsPerRevolution();
                NormalizeClipSettings(acceleratingSounds);
                NormalizeClipSettings(deceleratingSounds);
                if (!Application.isPlaying) { BuildRuntimeConfiguration(); BakeAllCurves(); return; }
                BakeAllCurves();
                Activate();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"VNS OnValidate error (likely mid-edit state): {e.Message}", this);
            }
        }

        private void NormalizeClipSettings(List<EngineAudioClipData> clips)
        {
            if (clips == null) return;
            for (int i = 0; i < clips.Count; i++)
            {
                EngineAudioClipData clip = clips[i];
                if (clip == null) continue;
                clip.rpmValue = Mathf.Max(1, clip.rpmValue);
                clip.loPitch = Mathf.Clamp(clip.loPitch, 0.01f, 3f);
                clip.hiPitch = Mathf.Clamp(clip.hiPitch, 0.01f, 3f);
            }
        }
        private void BuildRuntimeConfiguration()
        {
            combustionEventsPerRev = ComputeCombustionEventsPerRevolution();
            SanitizeClipLists();
            BuildRpmTables(acceleratingSounds, out AcMinrTable, out AcNormalrTable, out AcMaxrTable);
            BuildRpmTables(deceleratingSounds, out DcMinrTable, out DcNormalrTable, out DcMaxrTable);
            nonDecelerateAudiosMode = deceleratingSounds == null || deceleratingSounds.Count == 0;

            float highestClipRpm = 0f;
            if (AcNormalrTable != null && AcNormalrTable.Length > 0)
                highestClipRpm = Mathf.Max(highestClipRpm, AcNormalrTable[AcNormalrTable.Length - 1]);
            if (DcNormalrTable != null && DcNormalrTable.Length > 0)
                highestClipRpm = Mathf.Max(highestClipRpm, DcNormalrTable[DcNormalrTable.Length - 1]);

            _maxRpm = Mathf.Max(Mathf.Max(maximumTheoricalRPM, highestClipRpm), 1f);
            bool idleRpmNeedsRefresh = _idleRpm <= 0f || _idleRpm >= _maxRpm;
            _idleRpm = Mathf.Clamp(idleRpmNeedsRefresh ? InferIdleRpm() : _idleRpm, 0f, _maxRpm);
        }

        /// <summary>
        /// Removes only genuinely null list-element references from both banks.
        /// Entries whose AudioClip is not yet assigned are intentionally KEPT so
        /// that newly added (empty) inspector slots survive OnValidate and the
        /// user can assign a clip. All downstream consumers (BuildRpmTables,
        /// BuildBankLayers) already filter out null-clip entries themselves.
        /// </summary>
        private void SanitizeClipLists()
        {
            if (acceleratingSounds == null) acceleratingSounds = new List<EngineAudioClipData>();
            if (deceleratingSounds == null) deceleratingSounds = new List<EngineAudioClipData>();
            acceleratingSounds.RemoveAll(c => c == null);
            deceleratingSounds.RemoveAll(c => c == null);
        }

        private void BuildRpmTables(List<EngineAudioClipData> clips, out float[] minTable, out float[] normalTable, out float[] maxTable)
        {
            if (clips == null || clips.Count == 0) { minTable = Array.Empty<float>(); normalTable = Array.Empty<float>(); maxTable = Array.Empty<float>(); return; }

            var sorted = new List<EngineAudioClipData>(clips.Count);
            for (int i = 0; i < clips.Count; i++)
            {
                EngineAudioClipData c = clips[i];
                if (c != null && c.audioClip != null) sorted.Add(c);
            }
            for (int i = 1; i < sorted.Count; i++)
            {
                EngineAudioClipData key = sorted[i];
                int j = i - 1;
                while (j >= 0 && sorted[j].rpmValue > key.rpmValue)
                {
                    sorted[j + 1] = sorted[j];
                    j--;
                }
                sorted[j + 1] = key;
            }
            int count = sorted.Count;
            if (count == 0) { minTable = Array.Empty<float>(); normalTable = Array.Empty<float>(); maxTable = Array.Empty<float>(); return; }

            minTable = new float[count];
            normalTable = new float[count];
            maxTable = new float[count];

            for (int i = 0; i < count; i++) normalTable[i] = Mathf.Max(1f, sorted[i].rpmValue);

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
            BuildBankLayers(acceleratingSounds, accLayers, "ACC");
            BuildBankLayers(deceleratingSounds, decLayers, "DEC");

            // Only create one-shot pools when the feature is on and clips exist.
            if (enableExhaustBurble && burbleSounds != null && burbleSounds.Length > 0)
                BuildBurblePool();
            else
                burbleAudioSources.Clear();

            if (enableDctShiftBurble && dctShiftBurbleSound != null)
                BuildDctShiftBurbleSource();
            else
                ClearDctShiftBurbleRefs();

            bool hasIntakeRoar = intakeRoarSounds != null && intakeRoarSounds.Length > 0;
            bool hasFlutter = throttleFlutterSounds != null && throttleFlutterSounds.Length > 0;
            if (enableThrottleBody && (hasIntakeRoar || hasFlutter))
                BuildThrottleBodyPool();
            else
            {
                intakeRoarAudioSources.Clear();
                throttleFlutterAudioSources.Clear();
            }

            if (enableRedlineEffect && redlineSounds != null && redlineSounds.Length > 0)
                BuildRedlinePool();
            else
                redlineAudioSources.Clear();

            accTargetVolumes = new float[accLayers.Count];
            accTargetPitches = new float[accLayers.Count];
            decTargetVolumes = new float[decLayers.Count];
            decTargetPitches = new float[decLayers.Count];
            accBlendState = default;
            decBlendState = default;
            dctShiftBurbleStopTime = 0f;
#if !UNITY_WEBGL
            VehicleNoiseBatchManager.NotifyLayoutChanged(this);
#endif
        }

        private void BuildBankLayers(List<EngineAudioClipData> clips, List<RuntimeLayer> runtime, string prefix)
        {
            runtime.Clear();
            if (clips == null) return;

            // Sort clips by reference RPM (insertion sort; no LINQ).
            var sorted = new List<EngineAudioClipData>(clips.Count);
            for (int i = 0; i < clips.Count; i++)
            {
                EngineAudioClipData c = clips[i];
                if (c != null && c.audioClip != null) sorted.Add(c);
            }
            for (int i = 1; i < sorted.Count; i++)
            {
                EngineAudioClipData key = sorted[i];
                int j = i - 1;
                while (j >= 0 && sorted[j].rpmValue > key.rpmValue)
                {
                    sorted[j + 1] = sorted[j];
                    j--;
                }
                sorted[j + 1] = key;
            }

            bool useLpf = NeedsLowPassFilter();
            bool useHpf = NeedsHighPassFilter();
            bool useDist = NeedsDistortionFilter();
            bool useChorus = NeedsChorusFilter();
            bool useReverb = NeedsPerSourceReverbFilter();

            for (int i = 0; i < sorted.Count; i++)
            {
                EngineAudioClipData clipData = sorted[i];
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

                // Only add DSP components that can actually affect audio (strength > 0).
                // Avoids idle DSP graph cost when a filter path is unused.
                RuntimeLayer layer = new RuntimeLayer
                {
                    host = host,
                    source = source,
                    lowPass = useLpf ? host.AddComponent<AudioLowPassFilter>() : null,
                    highPass = useHpf ? host.AddComponent<AudioHighPassFilter>() : null,
                    distortion = useDist ? host.AddComponent<AudioDistortionFilter>() : null,
                    chorus = useChorus ? host.AddComponent<AudioChorusFilter>() : null,
                    reverb = useReverb ? host.AddComponent<AudioReverbFilter>() : null,
                    clip = clipData.audioClip,
                    referenceRpm = Mathf.Max(1f, clipData.rpmValue),
                    minPitch = clipData.loPitch,
                    maxPitch = clipData.hiPitch,
                    volumeOffset = clipData.volumeOffset,
                    pitchOffset = clipData.pitchOffset
                };
                InitializeFilterDefaults(layer, useLpf, useHpf, useDist, useChorus, useReverb);
                runtime.Add(layer);
                if (keepBankClipsPlaying) source.Play();
            }
        }

        private bool NeedsLowPassFilter() =>
            lowPassIntensity > 0.0001f || lowPassStrength > 0.0001f ||
            mufflingIntensity > 0.0001f || resonanceStrength > 0.0001f;

        private bool NeedsHighPassFilter() => highPassStrength > 0.0001f;

        // Job: distDrive * distortionIntensity * (1 + distortionStrength)
        private bool NeedsDistortionFilter() => distortionIntensity > 0.0001f;

        private bool NeedsChorusFilter() => chorusStrength > 0.0001f;

        private bool NeedsPerSourceReverbFilter() =>
            reverbStrength > 0.0001f && !(useSharedMixerReverb && mixer != null);

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

        private void ClearDctShiftBurbleRefs()
        {
            dctShiftBurbleSource = null;
            dctShiftBurbleLowPass = null;
            dctShiftBurbleHighPass = null;
            dctShiftBurbleDistortion = null;
            dctShiftBurbleChorus = null;
        }

        private void BuildDctShiftBurbleSource()
        {
            GameObject host = new GameObject("DCTSHIFTBURBLE");
            host.transform.SetParent(transform, false);
            AudioSource source = host.AddComponent<AudioSource>();
            ApplyTemplateToAudioSource(source);
            source.loop = false;
            source.playOnAwake = false;
            source.volume = 0f;
            source.pitch = dctShiftBurbleBasePitch;
            dctShiftBurbleSource = source;

            // Light LPF only for the DCT overlay envelope path. Distortion/chorus
            // were always forced off at runtime — do not pay for those components.
            dctShiftBurbleLowPass = host.AddComponent<AudioLowPassFilter>();
            dctShiftBurbleLowPass.enabled = true;
            dctShiftBurbleLowPass.cutoffFrequency = 22000f;
            dctShiftBurbleLowPass.lowpassResonanceQ = 1f;

            dctShiftBurbleHighPass = null;
            dctShiftBurbleDistortion = null;
            dctShiftBurbleChorus = null;
        }

        private void BuildRedlinePool()
        {
            redlineAudioSources.Clear();
            for (int i = 0; i < REDLINEAUDIOPOOLSIZE; i++)
            {
                GameObject host = new GameObject($"REDLINE{i:00}");
                host.transform.SetParent(transform, false);
                AudioSource src = host.AddComponent<AudioSource>();
                ApplyTemplateToAudioSource(src);
                src.loop = false;
                src.playOnAwake = false;
                src.volume = 0f;
                redlineAudioSources.Add(src);
            }
        }

        private void BuildThrottleBodyPool()
        {
            intakeRoarAudioSources.Clear();
            throttleFlutterAudioSources.Clear();
            for (int i = 0; i < THROTTLEBODYAUDIOPOOLSIZE; i++)
            {
                GameObject roarHost = new GameObject($"INTAKEROAR{i:00}");
                roarHost.transform.SetParent(transform, false);
                AudioSource roarSrc = roarHost.AddComponent<AudioSource>();
                ApplyTemplateToAudioSource(roarSrc);
                roarSrc.loop = false;
                roarSrc.playOnAwake = false;
                roarSrc.volume = 0f;
                intakeRoarAudioSources.Add(roarSrc);

                GameObject flutterHost = new GameObject($"THROTTLEFLUTTER{i:00}");
                flutterHost.transform.SetParent(transform, false);
                AudioSource flutterSrc = flutterHost.AddComponent<AudioSource>();
                ApplyTemplateToAudioSource(flutterSrc);
                flutterSrc.loop = false;
                flutterSrc.playOnAwake = false;
                flutterSrc.volume = 0f;
                throttleFlutterAudioSources.Add(flutterSrc);
            }
        }

        private void InitializeFilterDefaults(
            RuntimeLayer layer,
            bool useLpf, bool useHpf, bool useDist, bool useChorus, bool useReverb)
        {
            // Only components that were actually added are non-null.
            if (layer.lowPass != null)
            {
                layer.lowPass.enabled = useLpf;
                layer.lowPass.cutoffFrequency = 22000f;
                layer.lowPass.lowpassResonanceQ = 1f;
            }
            if (layer.highPass != null)
            {
                layer.highPass.enabled = useHpf;
                layer.highPass.cutoffFrequency = 10f;
                layer.highPass.highpassResonanceQ = 1f;
            }
            if (layer.distortion != null)
            {
                layer.distortion.enabled = useDist;
                layer.distortion.distortionLevel = 0f;
            }
            if (layer.chorus != null)
            {
                layer.chorus.enabled = useChorus;
                layer.chorus.dryMix = 1f;
                layer.chorus.wetMix1 = 0f;
                layer.chorus.wetMix2 = 0f;
                layer.chorus.wetMix3 = 0f;
                layer.chorus.delay = 20f;
                layer.chorus.rate = 0.8f;
                layer.chorus.depth = 0f;
            }
            if (layer.reverb != null)
            {
                layer.reverb.enabled = useReverb;
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
            dctShiftBurbleStopTime = 0f;
            if (dctShiftBurbleSource != null)
            {
                dctShiftBurbleSource.Stop();
                dctShiftBurbleSource.volume = 0f;
            }
            dctShiftFadeState = DctShiftFadeState.Idle;
            calculationRoutine = StartCoroutine(CalculateAsync());
        }

        private void StopProcessing()
        {
            if (calculationRoutine != null) { StopCoroutine(calculationRoutine); calculationRoutine = null; }
        }

        // Reused yield instruction (creating a new WaitForFixedUpdate each frame allocates).
        private static readonly WaitForFixedUpdate sWaitFixedUpdate = new WaitForFixedUpdate();

        private IEnumerator CalculateAsync()
        {
            while (true)
            {
                yield return sWaitFixedUpdate;
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
                VehicleNoiseBatchManager.ExecuteBatchIfNeeded(deltaTime);
                ApplySlewAndEffects(deltaTime);
#endif
            }
        }

#if UNITY_WEBGL
        /// <summary>
        /// WebGL audio tick (main thread). Same algorithm as the desktop Burst job + slew path.
        /// </summary>
        private void ProcessAudioFrame(float deltaTime)
        {
            float clampedRpm = Mathf.Clamp(smoothedRpm, 0f, Mathf.Max(1f, _maxRpm));
            float normalizedRpm = Mathf.InverseLerp(Mathf.Max(0f, _idleRpm), Mathf.Max(_idleRpm + 1f, _maxRpm), clampedRpm);

            float curvePitch = SampleBakedCurve(bakedPitchCurve, normalizedRpm);
            float loadPitchContrib = smoothedLoad * loadEffectivenessOnPitch;
            finalPitch = Mathf.Max(0.01f, curvePitch + loadPitchContrib + shiftPitchOsc);

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

            EvaluateBankTargets(accLayers, accTargetVolumes, accTargetPitches, finalPitch, finalAccVol, clampedRpm, maxVolumeAcc, acPitchTrim, true, ref accBlendState);
            EvaluateBankTargets(decLayers, decTargetVolumes, decTargetPitches, finalPitch, finalDecVol, clampedRpm, maxVolumeDcc, dcPitchTrim, false, ref decBlendState);

            ApplyTargetsToBank(accLayers, accTargetVolumes, accTargetPitches, deltaTime);
            ApplyTargetsToBank(decLayers, decTargetVolumes, decTargetPitches, deltaTime);
            ApplyPostEffectsToBank(accLayers, accTargetVolumes, normalizedRpm, deltaTime);
            ApplyPostEffectsToBank(decLayers, decTargetVolumes, normalizedRpm, deltaTime);
            UpdateBurble(deltaTime, clampedRpm);
            UpdateRedlineEffect(clampedRpm);
            ApplyPostEffectsToDctShiftSource(normalizedRpm, deltaTime);
            UpdateDiagnostics(deltaTime);
        }

        private void EvaluateBankTargets(
            List<RuntimeLayer> bank, float[] targetVolumes, float[] targetPitches,
            float finalPitch, float bankFinalVol, float clampedRpm,
            float bankVolumeLimit, float bankPitchTrim, bool isAcc, ref BankBlendState blendState)
        {
            int count = bank.Count;
            for (int i = 0; i < count; i++) { targetVolumes[i] = 0f; targetPitches[i] = 1f; }
            if (count == 0) return;

            float loadGainRaw = isAcc
                ? Mathf.Lerp(loadVolumeChangerMinValue, 1f, smoothedLoad)
                : Mathf.Lerp(loadVolumeChangerMinValue, 1f, 1f - smoothedLoad);
            float loadGainFactor = isAcc ? loadVolumeAccChangerFactor : loadVolumeDccChangerFactor;
            float loadGain = Mathf.Lerp(1f, loadGainRaw, loadGainFactor);
            float bankBaseGain = masterVolume * bankFinalVol * Mathf.Max(0f, loadGain);

            FindStableNeighbourPair(bank, clampedRpm, ref blendState, out int lo, out int hi);

            float stretch = Mathf.Max(1f, maxPitchRatioBeyondPair);
            float pitchRpm = ClampRpmForPairPitchMain(clampedRpm, bank[lo].referenceRpm, bank[hi].referenceRpm, stretch);

            if (lo == hi)
            {
                RuntimeLayer single = bank[lo];
                float g = bankBaseGain * Mathf.Max(0f, 1f + single.volumeOffset);
                if (bankVolumeLimit > 0f) g = Mathf.Min(g, bankVolumeLimit);
                targetVolumes[lo] = g;
                targetPitches[lo] = EvaluateLayerPitch(single, pitchRpm, finalPitch, bankPitchTrim);
                return;
            }

            float t = Mathf.InverseLerp(bank[lo].referenceRpm, bank[hi].referenceRpm, clampedRpm);
            float angle = t * Mathf.PI * 0.5f;
            float lowW = Mathf.Cos(angle);
            float highW = Mathf.Sin(angle);

            RuntimeLayer lLo = bank[lo], lHi = bank[hi];
            float gLo = bankBaseGain * lowW * Mathf.Max(0f, 1f + lLo.volumeOffset);
            float gHi = bankBaseGain * highW * Mathf.Max(0f, 1f + lHi.volumeOffset);

            if (bankVolumeLimit > 0f) { gLo = Mathf.Min(gLo, bankVolumeLimit); gHi = Mathf.Min(gHi, bankVolumeLimit); }

            targetVolumes[lo] = gLo;
            targetVolumes[hi] = gHi;
            targetPitches[lo] = EvaluateLayerPitch(lLo, pitchRpm, finalPitch, bankPitchTrim);
            targetPitches[hi] = EvaluateLayerPitch(lHi, pitchRpm, finalPitch, bankPitchTrim);
        }

        private float EvaluateLayerPitch(RuntimeLayer layer, float pitchRpm, float finalPitch, float bankPitchTrim)
        {
            float referenceRpm = Mathf.Max(1f, layer.referenceRpm);
            float ratioPitch = (pitchRpm / referenceRpm) * finalPitch;
            float progress = Mathf.Clamp01((pitchRpm - _idleRpm) / Mathf.Max(1f, _maxRpm - _idleRpm));
            float hiLoMul = Mathf.Lerp(layer.minPitch, layer.maxPitch, progress);
            float pitch = ratioPitch * hiLoMul + bankPitchTrim + layer.pitchOffset;
            return Mathf.Clamp(pitch, 0.01f, 3f);
        }

        private static float ClampRpmForPairPitchMain(
            float clampedRpm, float pairLoRef, float pairHiRef, float stretch)
        {
            float minRef = Mathf.Min(pairLoRef, pairHiRef);
            float maxRef = Mathf.Max(pairLoRef, pairHiRef);
            minRef = Mathf.Max(1f, minRef);
            maxRef = Mathf.Max(minRef, maxRef);
            float s = stretch > 1f ? stretch : 1f;
            return Mathf.Clamp(clampedRpm, minRef / s, maxRef * s);
        }

        /// <summary>Neighbour-pair hold and hysteresis (WebGL; keep in sync with Burst EvaluateBankInJob).</summary>
        private void FindStableNeighbourPair(List<RuntimeLayer> bank, float clampedRpm, ref BankBlendState state, out int lo, out int hi)
        {
            int count = bank.Count;
            if (count <= 0) { lo = hi = 0; return; }
            if (count == 1)
            {
                lo = hi = 0;
                if (!state.initialized)
                {
                    state.initialized = true;
                    state.lowIndex = 0;
                    state.highIndex = 0;
                    state.holdUntilTime = Time.time + QuantizeHoldToTicksMain();
                }
                return;
            }

            FindImmediateNeighbourPair(bank, clampedRpm, out int idealLo, out int idealHi);

            int curLo = state.lowIndex < count ? state.lowIndex : count - 1;
            int curHi = state.highIndex < count ? state.highIndex : count - 1;
            float stretch = Mathf.Max(1f, maxPitchRatioBeyondPair);
            float currentTime = Time.time;

            if (!state.initialized)
            {
                lo = idealLo;
                hi = idealHi;
                state.initialized = true;
                state.lowIndex = lo;
                state.highIndex = hi;
                state.holdUntilTime = currentTime + QuantizeHoldToTicksMain();
                return;
            }

            bool wantsSwitch = idealLo != curLo || idealHi != curHi;
            bool inHold = currentTime < state.holdUntilTime;
            int stepGap = Mathf.Max(Mathf.Abs(idealHi - curHi), Mathf.Abs(idealLo - curLo));
            bool multiStepBehind = stepGap > 1;

            bool stretchEscape = false;
            if (inHold || wantsSwitch)
            {
                float pairMin = Mathf.Min(bank[curLo].referenceRpm, bank[curHi].referenceRpm);
                float pairMax = Mathf.Max(bank[curLo].referenceRpm, bank[curHi].referenceRpm);
                stretchEscape =
                    clampedRpm > pairMax * stretch ||
                    clampedRpm < pairMin / stretch;
            }

            bool forceIdeal = multiStepBehind || stretchEscape;

            if (inHold && !forceIdeal)
            {
                if (enablePairSelectorDiagnostics) _pairDiagAccHoldBlocked++;
                lo = curLo;
                hi = curHi;
                return;
            }

            bool passesHysteresis = false;
            if (wantsSwitch)
            {
                if (idealHi > curHi)
                    passesHysteresis = clampedRpm > bank[curHi].referenceRpm + pairHysteresisRpm;
                else if (idealHi < curHi)
                    passesHysteresis = clampedRpm < bank[curLo].referenceRpm - pairHysteresisRpm;
                else
                    passesHysteresis = true;
            }

            if (wantsSwitch && (passesHysteresis || forceIdeal))
            {
                float holdDuration = QuantizeHoldToTicksMain();
                if (enablePairSelectorDiagnostics)
                {
                    float ftWeb = Time.fixedDeltaTime > 0.00001f ? Time.fixedDeltaTime : 0.02f;
                    float rawHold = (combustionEventsPerRev > 0f && smoothedRpm > 0f)
                        ? pairHoldCycles / (smoothedRpm / 60f * combustionEventsPerRev)
                        : 0f;
                    int holdTicks = Mathf.RoundToInt(holdDuration / ftWeb);
                    if (Time.time >= _pairDiagLogTime + 1f)
                    {
                        _pairDiagLogTime = Time.time;
                        Debug.Log(
                            $"[VNS PairDiag WebGL | {name}] RPM={clampedRpm:0} " +
                            $"combustionEventsPerRev={combustionEventsPerRev:0.0} " +
                            $"rawHold={rawHold * 1000f:0.00}ms effHold={holdDuration * 1000f:0.00}ms ({holdTicks} ticks) " +
                            $"fixedDt={ftWeb * 1000f:0.00}ms " +
                            $"pairHysteresisRpm={pairHysteresisRpm:0} " +
                            $"switches/s={_pairDiagAccSwitches} " +
                            $"hystBlocked/s={_pairDiagAccHystBlocked} " +
                            $"holdBlocked/s={_pairDiagAccHoldBlocked}",
                            this);
                        _pairDiagAccSwitches = 0;
                        _pairDiagAccHystBlocked = 0;
                        _pairDiagAccHoldBlocked = 0;
                    }
                    _pairDiagAccSwitches++;
                }

                state.lowIndex = idealLo;
                state.highIndex = idealHi;
                state.holdUntilTime = currentTime + holdDuration;
                lo = idealLo;
                hi = idealHi;
            }
            else
            {
                if (enablePairSelectorDiagnostics && wantsSwitch) _pairDiagAccHystBlocked++;
                lo = curLo;
                hi = curHi;
            }
        }

        private float QuantizeHoldToTicksMain()
        {
            float ft = Time.fixedDeltaTime > 0.00001f ? Time.fixedDeltaTime : 0.02f;
            float rawHold = (combustionEventsPerRev > 0f && smoothedRpm > 0f)
                ? pairHoldCycles / (smoothedRpm / 60f * combustionEventsPerRev)
                : 0f;
            return Mathf.Ceil(rawHold / ft) * ft;
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
#endif

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

            float slew2000 = filterLPFSlewHz * deltaTime;
            float slew180 = filterHPFSlewHz * deltaTime;
            float slew2500 = filterReverbSlewDbS * deltaTime;
            float slewBase = filterParamSlewRate * deltaTime;

            for (int i = 0; i < bank.Count; i++)
            {
                RuntimeLayer layer = bank[i];
                float tv = i < targetVolumes.Length ? targetVolumes[i] : 0f;
                float activity = Mathf.Max(layer.source.volume, tv);
                if (activity < 0.0005f) continue;

                if (layer.lowPass != null && layer.lowPass.enabled)
                {
                    layer.lowPass.cutoffFrequency = Mathf.MoveTowards(layer.lowPass.cutoffFrequency, lowPassTarget, slew2000);
                    layer.lowPass.lowpassResonanceQ = Mathf.MoveTowards(layer.lowPass.lowpassResonanceQ, lowPassQ, slewBase);
                }
                if (layer.highPass != null && layer.highPass.enabled)
                {
                    layer.highPass.cutoffFrequency = Mathf.MoveTowards(layer.highPass.cutoffFrequency, highPassTarget, slew180);
                    layer.highPass.highpassResonanceQ = Mathf.MoveTowards(layer.highPass.highpassResonanceQ, highPassQ, slewBase);
                }
                if (layer.distortion != null && layer.distortion.enabled)
                    layer.distortion.distortionLevel = Mathf.MoveTowards(layer.distortion.distortionLevel, distortionTarget, slewBase);
                if (layer.chorus != null && layer.chorus.enabled)
                {
                    layer.chorus.dryMix = Mathf.MoveTowards(layer.chorus.dryMix, chorusDry, slewBase);
                    layer.chorus.wetMix1 = Mathf.MoveTowards(layer.chorus.wetMix1, chorusWet * 0.6f, slewBase);
                    layer.chorus.wetMix2 = Mathf.MoveTowards(layer.chorus.wetMix2, chorusWet * 0.3f, slewBase);
                    layer.chorus.wetMix3 = Mathf.MoveTowards(layer.chorus.wetMix3, chorusWet * 0.1f, slewBase);
                    layer.chorus.depth = Mathf.MoveTowards(layer.chorus.depth, chorusDepth, slewBase);
                    layer.chorus.rate = Mathf.MoveTowards(layer.chorus.rate, chorusRate, slewBase);
                }
                if (layer.reverb != null && layer.reverb.enabled)
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
            for (int i = 0; i < burbleAudioSources.Count; i++)
            {
                AudioSource src = burbleAudioSources[i];
                if (src.isPlaying) src.volume = Mathf.Max(0f, src.volume - burbleFadeRate * deltaTime);
            }

            if (!enableExhaustBurble)
            {
                UpdateDctShiftBurble();
                return;
            }

            bool rpmCondition = clampedRpm >= burbleMinRPM;
            bool loadCondition = smoothedLoad >= burbleLoadLowThreshold && smoothedLoad < burbleLoadHighThreshold;
            bool rpmDropCond = burbleRPMDropThreshold > 0f && (previousSmoothedRpm - smoothedRpm) >= burbleRPMDropThreshold;
            bool burbleEligible = rpmCondition && (loadCondition || rpmDropCond);

            if (burbleSounds == null || burbleSounds.Length == 0)
            {
                UpdateDctShiftBurble();
                return;
            }
            if (Time.time < nextBurbleTime)
            {
                UpdateDctShiftBurble();
                return;
            }

            if (!burbleEligible)
            {
                UpdateDctShiftBurble();
                return;
            }

            if (enableBurbleDiagnostics)
                Debug.Log($"VNS-Burble: rpm={clampedRpm:0} load={smoothedLoad:0.00} loadOk={loadCondition} rpmDrop={previousSmoothedRpm - smoothedRpm:0} rpmDropOk={rpmDropCond}");

            if (UnityEngine.Random.value > burbleProbability)
            {
                UpdateDctShiftBurble();
                return;
            }

            AudioSource freeSource = GetFreeBurbleSource();
            if (freeSource == null)
            {
                UpdateDctShiftBurble();
                return;
            }

            int clipIndex = UnityEngine.Random.Range(0, burbleSounds.Length);
            freeSource.Stop();
            freeSource.clip = burbleSounds[clipIndex];
            freeSource.volume = burbleVolume;
            freeSource.pitch = 1f + UnityEngine.Random.Range(-burbleRandomPitchVariation, burbleRandomPitchVariation);
            freeSource.Play();
            nextBurbleTime = Time.time + UnityEngine.Random.Range(minBurbleDelay, maxBurbleDelay);
            UpdateDctShiftBurble();
        }

        /// <summary>Stops the DCT shift burble source once its max duration has elapsed.</summary>
        private void UpdateDctShiftBurble()
        {
            if (dctShiftBurbleSource == null) return;

            UpdateDctShiftFade();

            if (!dctShiftBurbleSource.isPlaying) return;

            if (Time.time >= dctShiftBurbleStopTime)
            {
                if (dctShiftFadeState != DctShiftFadeState.FadeOut)
                    BeginDctShiftFade(DctShiftFadeState.FadeOut, dctShiftBurbleSource.volume);
                return;
            }

            // Short grace period so gear-engagement noise does not kill the burble immediately.
            float burbleElapsed = Time.time - (dctShiftBurbleStopTime - dctShiftBurbleMaxDuration);
            if (burbleElapsed > 0.03f)
            {
                float deltaLoad = smoothedLoad - previousSmoothedLoad;
                if (deltaLoad > 0f && dctShiftFadeState != DctShiftFadeState.FadeOut)
                    BeginDctShiftFade(DctShiftFadeState.FadeOut, dctShiftBurbleSource.volume);
            }
        }

        /// <summary>Drives the fade-in / sustain / fade-out envelope and stops the source once faded out.</summary>
        private void UpdateDctShiftFade()
        {
            if (dctShiftFadeState == DctShiftFadeState.Idle) return;
            if (dctShiftBurbleSource == null) return;

            float now = Time.time;
            float t = Mathf.Clamp01((now - dctShiftBurbleFadeStartTime) / DctShiftFadeDuration);

            switch (dctShiftFadeState)
            {
                case DctShiftFadeState.FadeIn:
                    dctShiftBurbleSource.volume = Mathf.Lerp(dctShiftBurbleFadeStartVolume, dctShiftBurbleTargetVolume, t);
                    if (t >= 1f) dctShiftFadeState = DctShiftFadeState.Sustain;
                    break;
                case DctShiftFadeState.Sustain:
                    dctShiftBurbleSource.volume = dctShiftBurbleTargetVolume;
                    break;
                case DctShiftFadeState.FadeOut:
                    dctShiftBurbleSource.volume = Mathf.Lerp(dctShiftBurbleFadeStartVolume, 0f, t);
                    if (t >= 1f)
                    {
                        dctShiftBurbleSource.volume = 0f;
                        dctShiftBurbleSource.Stop();
                        dctShiftFadeState = DctShiftFadeState.Idle;
                    }
                    break;
            }
        }

        private void ApplyPostEffectsToDctShiftSource(float normalizedRpm, float deltaTime)
        {
        }

        /// <summary>
        /// Fires one-shot redline exhaust clips in a user-defined loop while the
        /// engine RPM stays within the configured redline window. Clips are
        /// fire-and-forget from a small pool; delay and pitch are randomised
        /// each iteration to avoid a mechanical, metronomic feel.
        /// </summary>
        private void UpdateRedlineEffect(float clampedRpm)
        {
            if (!enableRedlineEffect || redlineSounds == null || redlineSounds.Length == 0) return;

            float ceiling = redlineMaxRPM > 0f ? redlineMaxRPM : _maxRpm;
            bool inRange = clampedRpm >= redlineMinRPM && clampedRpm <= ceiling;

            if (!inRange) return;
            if (Time.time < nextRedlineTime) return;

            AudioSource src = GetFreeRedlineSource();
            if (src == null) return;

            src.clip = redlineSounds[UnityEngine.Random.Range(0, redlineSounds.Length)];
            src.volume = redlineVolume;
            src.pitch = redlineBasePitch +
                         UnityEngine.Random.Range(-redlinePitchVariation, redlinePitchVariation);
            src.Play();
            nextRedlineTime = Time.time +
                              UnityEngine.Random.Range(redlineMinDelay, redlineMaxDelay);
        }

        private AudioSource GetFreeRedlineSource()
        {
            if (redlineAudioSources.Count == 0) return null;
            for (int i = 0; i < redlineAudioSources.Count; i++)
                if (!redlineAudioSources[i].isPlaying) return redlineAudioSources[i];
            AudioSource quietest = redlineAudioSources[0];
            for (int i = 1; i < redlineAudioSources.Count; i++)
                if (redlineAudioSources[i].volume < quietest.volume) quietest = redlineAudioSources[i];
            return quietest;
        }

        private AudioSource GetFreeThrottleBodySource(List<AudioSource> pool)
        {
            if (pool == null || pool.Count == 0) return null;
            for (int i = 0; i < pool.Count; i++)
                if (!pool[i].isPlaying) return pool[i];
            AudioSource quietest = pool[0];
            for (int i = 1; i < pool.Count; i++)
                if (pool[i].volume < quietest.volume) quietest = pool[i];
            return quietest;
        }

        private AudioSource GetFreeBurbleSource()
        {
            if (burbleAudioSources.Count == 0) return null;
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
            if (dctShiftBurbleSource != null && dctShiftBurbleSource.isPlaying)
            {
                dctShiftBurbleSource.Stop();
                dctShiftBurbleSource.volume = 0f;
            }
            dctShiftFadeState = DctShiftFadeState.Idle;
            dctShiftBurbleStopTime = 0f;
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
            DestroyPool(intakeRoarAudioSources);
            DestroyPool(throttleFlutterAudioSources);
            DestroyPool(redlineAudioSources);
            if (dctShiftBurbleSource != null)
            {
                SafeDestroy(dctShiftBurbleSource.gameObject);
                dctShiftBurbleSource = null;
            }
            ClearDctShiftBurbleRefs();
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
