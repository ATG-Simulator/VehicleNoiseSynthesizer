using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Audio;
using System.Collections;
using System.Linq;

namespace AroundTheGroundSimulator
{
    [AddComponentMenu("ATG/Audio/Vehicle Noise Synthesizer")]
    [HelpURL("https://github.com/ImDanOush/VehicleNoiseSynthesizer")]
    public class VehicleNoiseSynthesizer : MonoBehaviour
    {
        #region Debug Controls
        [Header("Debug Controls")]
        [Space(5)]
        [Tooltip("Enable to manually control RPM and load values for testing")]
        public bool _debug = false;

        [Range(100f, 9000f)]
        [Tooltip("Test RPM value when debug mode is enabled")]
        public float debug_rpm = 800;

        [Range(0.00f, 1.00f)]
        [Tooltip("Test engine load value when debug mode is enabled")]
        public float debug_load = 1;

        [Tooltip("Enable the 1-second diagnostic logger.  Prints RPM, load, per-source volume/pitch, and active filter values to the Unity console.  Disable in production.")]
        public bool enableDiagnosticLogger = false;
        #endregion

        #region Core Audio Settings
        [Header("Core Audio Settings")]
        [Space(10)]
        [Tooltip("Fine-tune the overall pitch of engine sounds")]
        public float targetedShiftPitch = 0;

        [HideInInspector]
        public float shiftPitchOsc = 0;

        [Range(0.007f, 1.00f)]
        [Tooltip("Master volume control for all engine sounds")]
        public float masterVolume = 1;

        [Range(0.000f, 1.00f)]
        public float loadVolumeAccChangerFactor = 1;

        [Range(0.000f, 1.00f)]
        public float loadVolumeDccChangerFactor = 1;

        [Range(0.00f, 0.99f)]
        public float loadVolumeChangerMinValue = 1;

        public bool autoBlip = true;

        [Tooltip("Template AudioSource for copying base audio settings")]
        public AudioSource audioSourceTemplate;

        [Tooltip("Optional mixer group for audio routing")]
        public AudioMixerGroup mixer;

        [Tooltip("Type of engine sound (Intake, Engine, Exhaust, etc.)")]
        public MixerType mixerType;

        [Tooltip("RPM difference between consecutive audio clips")]
        public int rpm_deviation = 1000;
        #endregion

        #region Sound Curves
        [Header("Engine Sound Response Curves")]
        [Space(10)]
        [Tooltip("Controls how pitch changes with RPM (X: Normalized RPM, Y: Pitch multiplier)")]
        public AnimationCurve pitchCurve = new AnimationCurve(
            new Keyframe(0f, 0.6f),
            new Keyframe(1f, 1.2f)
        );

        [Tooltip("How many times the engine load can change the pitch?")]
        public float loadEffectivenessOnPitch = 1;

        [Tooltip("Controls how volume changes with RPM (X: Normalized RPM, Y: Volume multiplier)")]
        public AnimationCurve volumeCurve = new AnimationCurve(
            new Keyframe(0f, 0.5f),
            new Keyframe(1f, 1f)
        );
        #endregion

        #region Audio Effect Settings
        [Header("Audio Effects Configuration")]
        [Space(10)]
        [Tooltip("Controls distortion amount based on RPM and load")]
        public AnimationCurve distortionCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.7f, 0.3f),
            new Keyframe(1f, 0.5f)
        );

        [Range(0f, 1f)]
        [Tooltip("Overall intensity of the distortion effect")]
        public float distortionIntensity = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("Intensity of the muffling effect when engine load decreases")]
        public float mufflingIntensity = 0.5f;

        [Header("Low Pass Filter")]
        [Space(5)]
        [Tooltip("Controls frequency cutoff based on load (X: Load, Y: Cutoff frequency)")]
        public AnimationCurve lowPassCurve = new AnimationCurve(
            new Keyframe(1f, 1000f),
            new Keyframe(0f, 22000f)
        );

        [Range(0f, 1f)]
        [Tooltip("Overall intensity of the low pass filter effect")]
        public float lowPassIntensity = 0.5f;
        #endregion

        #region Volume and Audio Clip Settings
        [System.Serializable]
        public class EngineAudioClipData
        {
            [Tooltip("Audio clip containing engine sound")]
            public AudioClip audioClip;

            [Tooltip("RPM value at which this audio clip should play")]
            [Range(0, 10000)]
            public int rpmValue;

            [Tooltip("Volume Offset")]
            [Range(-1.0f, 1.0f)]
            public float volumeOffset;

            [Tooltip("Pitch Offset")]
            [Range(-1.0f, 1.0f)]
            public float pitchOffset;

            [Tooltip("Optional description for this audio clip")]
            public string description;
        }

        [Header("Exhaust Burble Configuration")]
        [Space(10)]
        [Tooltip("Enable exhaust burble/overrun effect")]
        public bool enableExhaustBurble = true;

        [Tooltip("Audio clips for exhaust burble/overrun sounds")]
        public AudioClip[] burbleSounds;

        [Range(0f, 1f)]
        [Tooltip("Master volume for burble sounds")]
        public float burbleVolume = 0.7f;

        [Tooltip("Minimum RPM required for burble to occur")]
        public float burbleMinRPM = 3500f;

        [Range(0f, 1f)]
        [Tooltip("How quickly load must drop to trigger burble")]
        public float burbleLoadThreshold = 0.3f;

        [Tooltip("RPM drop per tick required to trigger burble via the RPM-drop path. " +
                 "Covers WOT upshifts and rev-limiter cuts where throttle stays high " +
                 "so load never drops enough to trigger the load-delta path alone. " +
                 "Default: 500 RPM/tick. Set 0 to disable.")]
        [Range(0f, 3000f)]
        public float burbleRPMDropThreshold = 500f;

        [Range(0f, 1f)]
        [Tooltip("Chance of burble occurring when conditions are met")]
        public float burbleProbability = 0.7f;

        [Range(0.01f, 0.5f)]
        [Tooltip("Minimum delay between burble sounds")]
        public float minBurbleDelay = 0.05f;

        [Range(0.05f, 1f)]
        [Tooltip("Maximum delay between burble sounds")]
        public float maxBurbleDelay = 0.2f;

        [Range(0f, 0.2f)]
        [Tooltip("Amount of random pitch variation applied to each burble event. " +
                 "Keeps consecutive pops from sounding identical. 0 = no randomness.")]
        public float burbleRandomPitchVariation = 0.08f;

        [Header("Engine Lugging Configuration")]
        [Space(10)]
        [Tooltip("Enable engine lugging/straining sound effect")]
        public bool enableEngineLugging = true;

        [Tooltip("Audio clips for engine lugging sounds. These should ideally be loopable.")]
        public AudioClip[] luggingSounds;

        [Range(0f, 1f)]
        [Tooltip("Master volume for lugging sounds")]
        public float luggingVolume = 0.8f;

        [Tooltip("Minimum RPM for lugging effect to start (e.g., below idle or slightly above)")]
        public float luggingMinRPMThreshold = 800f;

        [Tooltip("Maximum RPM at which lugging effect can occur (e.g., 2000-2500 RPM)")]
        public float luggingMaxRPMThreshold = 2000f;

        [Tooltip("Minimum engine load (0-1) required to trigger lugging sound")]
        [Range(0f, 1f)]
        public float luggingMinLoadThreshold = 0.7f;

        [Tooltip("Speed at which lugging sound fades in")]
        [Range(0.1f, 20f)]
        public float luggingFadeInSpeed = 5f;

        [Tooltip("Speed at which lugging sound fades out")]
        [Range(0.1f, 20f)]
        public float luggingFadeOutSpeed = 3f;

        [Tooltip("Base pitch for lugging sounds")]
        [Range(0.5f, 1.5f)]
        public float luggingBasePitch = 0.9f;

        [Range(0f, 0.2f)]
        [Tooltip("Random pitch variation added on top of luggingBasePitch each time a new " +
                 "lugging clip starts. Prevents the looping clip from sounding mechanical. " +
                 "0 = no randomness.")]
        public float luggingRandomPitchVariation = 0.05f;

        [Header("Audio Clip Configuration")]
        [Space(10)]
        [SerializeField]
        [Tooltip("List of audio clips for engine acceleration, each with its corresponding RPM value")]
        public List<EngineAudioClipData> acceleratingSounds = new List<EngineAudioClipData>();

        [SerializeField]
        [Tooltip("Optional list of audio clips for engine deceleration, each with its corresponding RPM value")]
        public List<EngineAudioClipData> deceleratingSounds = new List<EngineAudioClipData>();

        [Header("Volume Configuration")]
        [Space(10)]
        [Tooltip("Default volume when engine is idle")]
        [Range(0.05f, 1.00f)]
        public float idleVolume = 0.1f;

        [Tooltip("Maximum volume for acceleration sounds (0 means no limit)")]
        [Range(0.00f, 2.00f)]
        public float maxVolumeAcc = 0.4f;

        [Tooltip("Maximum volume for deceleration sounds")]
        [Range(0.00f, 2.00f)]
        public float maxVolumeDcc = 0.1f;
        #endregion

        #region Fine-Tuning Parameters
        [Header("Fine-Tuning")]
        [Space(10)]
        [Tooltip("Fine-tune pitch for acceleration sounds")]
        [Range(-1.0f, 1.0f)]
        public float acPitchTrim = 0;

        [Tooltip("Fine-tune pitch for deceleration sounds")]
        [Range(-1.0f, 1.0f)]
        public float dcPitchTrim = 0;

        [Tooltip("Maximum theoretical RPM value for calculations")]
        [Range(1000f, 20000f)]
        public float maximumTheoricalRPM = 10000;

        [Tooltip("Base pitch value when engine is idle")]
        [Range(0.5f, 2f)]
        public float idlePitch = 1;

        [Header("Sound Character - Strength Controls")]
        [Range(0f, 1f)]
        [Tooltip("Low-Pass muffling BOOST.  0 = base curve behaviour only (lowPassIntensity drives muffling).  " +
                 "1 = deepens muffling further via mufflingIntensity for a heavier off-throttle exhaust character.")]
        public float lowPassStrength = 0f;

        [Range(0f, 1f)]
        [Tooltip("High-Pass (treble rasp) strength.  0 = full low-end preserved.  " +
                 "1 = aggressively cuts low-end at high RPM for a raspy/mechanical top end.")]
        public float highPassStrength = 0f;

        [Range(0f, 1f)]
        [Tooltip("Resonance (Q-peak at LP cutoff) strength.  0 = smooth LP curve.  " +
                 "1 = pronounced resonant 'honk' peaking at mid-RPM (exhaust resonance band).")]
        public float resonanceStrength = 0f;

        [Range(0f, 1f)]
        [Tooltip("Distortion BOOST.  0 = base distortionCurve x distortionIntensity only.  " +
                 "1 = doubles the base grit for aggressive exhaust rasp at high load/RPM.")]
        public float distortionStrength = 0f;

        [Range(0f, 1f)]
        [Tooltip("Chorus (exhaust chamber phasing) strength.  Adds a subtle chorus effect " +
                 "at mid-to-high RPM, simulating harmonic resonance inside the exhaust.")]
        public float chorusStrength = 0f;

        [Range(0f, 1f)]
        [Tooltip("Reverb mix strength.  Raises the reverb zone contribution at high RPM/load " +
                 "for a more spatial, tunnelling exhaust character.")]
        public float reverbStrength = 0f;

        [Header("Acc/Dec Crossfade")]
        [Space(5)]
        [Range(0f, 0.5f)]
        [Tooltip("Load value at which acceleration and deceleration layers are blended 50/50. " +
                 "NWH VP2 engines report a non-zero friction load even at zero throttle (~0.15-0.20). " +
                 "Set this above your engine's idle-coast load floor so dec clips are audible on lift-off.")]
        public float loadCrossoverPoint = 0.18f;

        [Range(0.01f, 0.4f)]
        [Tooltip("Width of the blend zone around loadCrossoverPoint. " +
                 "0.10 means full dec below (crossover - 0.05) and full acc above (crossover + 0.05). " +
                 "Larger values produce a longer, softer transition between acc and dec layers.")]
        public float loadBlendWidth = 0.10f;
        #endregion

        #region Internal State Variables
        [Header("Internal State")]
        [Space(10)]
        [SerializeField]
        [Tooltip("Current engine RPM")]
        private float _rpm;

        [SerializeField]
        [Tooltip("Current engine load")]
        private float _load;

        [SerializeField]
        [Tooltip("Maximum RPM of the engine")]
        private float _maxRpm;

        [SerializeField]
        [Tooltip("Engine state (on/off)")]
        private bool _isOn;

        [SerializeField]
        [Tooltip("Idle RPM value")]
        private float _idleRpm;

        [SerializeField]
        [Tooltip("Previous frame RPM value")]
        private float l_r;

        [SerializeField]
        [Tooltip("Final pitch value after all modifications")]
        private float _finalPitch = 1f;

        [SerializeField]
        [Tooltip("Current volume for acceleration sounds")]
        private float _finalAccVol = 0;

        [SerializeField]
        [Tooltip("Current volume for deceleration sounds")]
        private float _finalDecVol = 0;
        #endregion

        #region Audio Processing Tables
        [Header("Audio Processing Tables")]
        [Space(10)]
        [SerializeField]
        [Tooltip("Minimum RPM values for acceleration clips")]
        private float[] _AcMin_rTable;

        [SerializeField]
        [Tooltip("Normal RPM values for acceleration clips")]
        private float[] _AcNormal_rTable;

        [SerializeField]
        [Tooltip("Maximum RPM values for acceleration clips")]
        private float[] _AcMax_rTable;

        [SerializeField]
        [Tooltip("Minimum RPM values for deceleration clips")]
        private float[] _DcMin_rTable;

        [SerializeField]
        [Tooltip("Normal RPM values for deceleration clips")]
        private float[] _DcNormal_rTable;

        [SerializeField]
        [Tooltip("Maximum RPM values for deceleration clips")]
        private float[] _DcMax_rTable;

        private float[] _DcvTable;
        private float[] _AcvTable;
        private float[] _DcpTable;
        private float[] _AcpTable;

        [SerializeField]
        [Tooltip("True if no deceleration audio clips are provided")]
        private bool _nonDecelerateAudiosMode = true;

        [SerializeField]
        [Tooltip("Scale factor for audio clip fade ranges")]
        private float _RangeDivider = 1;
        #endregion

        #region Audio Sources
        private List<AudioSource> _accelerateAudios;
        private List<AudioSource> _decelerateAudios;
        private List<AudioSource> luggingAudioSources;
        private const int LUGGING_AUDIO_POOL_SIZE = 2;
        #endregion

        #region Public Properties
        public float rpm { get; internal set; }
        public float load { get; internal set; }
        #endregion

        [HideInInspector]
        public bool launchMode = false;

        private float lastLoad;
        private float lastBurbleTime;
        private float _lastBurbleRPM;
        private List<AudioSource> burbleAudioSources;
        private const int BURBLE_AUDIO_POOL_SIZE = 5;
        private float vol;
        private AudioLowPassFilter[] lowPassFilters;
        private AudioHighPassFilter[] highPassFilters;
        private AudioDistortionFilter[] distortionFilters;
        private AudioChorusFilter[] flangerFilters;

        private float currentLuggingEffectVolume = 0f;
        private float _nextLogTime = 0f;


        public enum MixerType
        {
            Intake,
            Engine,
            Exhaust,
            Transmission,
            Differential
        }

        internal void Activate(float maxRpm, float idleRpm)
        {
            _maxRpm = maxRpm;
            _idleRpm = idleRpm;

            _AcpTable = new float[acceleratingSounds.Count];
            _AcvTable = new float[acceleratingSounds.Count];
            _DcpTable = new float[deceleratingSounds.Count];
            _DcvTable = new float[deceleratingSounds.Count];

            _nonDecelerateAudiosMode = true;
            int counter = 0;
            _AcNormal_rTable = new float[acceleratingSounds.Count];
            foreach (EngineAudioClipData item in acceleratingSounds)
            {
                _AcNormal_rTable[counter] = item.rpmValue;
                _AcpTable[counter] = item.pitchOffset;
                _AcvTable[counter] = item.volumeOffset;
                counter++;
            }
            counter = 0;
            _DcNormal_rTable = new float[deceleratingSounds.Count];
            foreach (EngineAudioClipData item in deceleratingSounds)
            {
                _DcNormal_rTable[counter] = item.rpmValue;
                _DcpTable[counter] = item.pitchOffset;
                _DcvTable[counter] = item.volumeOffset;
                counter++;
            }
            if (counter != 0)
                _nonDecelerateAudiosMode = false;

            _AcMax_rTable = new float[_AcNormal_rTable.Length];
            _AcMin_rTable = new float[_AcNormal_rTable.Length];

            _AcMin_rTable[0] = 0f;
            _AcMax_rTable[0] = _AcNormal_rTable.Length > 0 ? _AcNormal_rTable[0] + rpm_deviation : 0f;

            for (int i = 1; i < _AcNormal_rTable.Length; i++)
            {
                _AcMax_rTable[i] = _AcNormal_rTable[i] + rpm_deviation;
                _AcMin_rTable[i] = _AcNormal_rTable[i] - rpm_deviation;
            }

            _DcMax_rTable = new float[_DcNormal_rTable.Length];
            _DcMin_rTable = new float[_DcNormal_rTable.Length];

            if (!_nonDecelerateAudiosMode)
            {
                _DcMin_rTable[0] = 0f;
                _DcMax_rTable[0] = _DcNormal_rTable.Length > 0 ? _DcNormal_rTable[0] + rpm_deviation : 0f;
            }

            for (int i = 1; i < _DcNormal_rTable.Length; i++)
            {
                _DcMax_rTable[i] = _DcNormal_rTable[i] + rpm_deviation;
                _DcMin_rTable[i] = _DcNormal_rTable[i] - rpm_deviation;
            }

            _accelerateAudios = new List<AudioSource>();
            _decelerateAudios = new List<AudioSource>();

            Transform T = this.gameObject.transform;

            if (acceleratingSounds.Count <= 0)
            {
                throw new System.Exception("No Audios provided!");
            }

            AudioSource carSound;
            List<Keyframe> ks = new List<Keyframe>()
            {
            new Keyframe(0,1),
            new Keyframe(32,0.256f),
            new Keyframe(128,0.0f)
        };
            AnimationCurve aC = new AnimationCurve(ks.ToArray());
            for (int i = 0; i < aC.length; i++)
            {
                aC.SmoothTangents(i, 0);
            }

            foreach (var sound in acceleratingSounds)
            {
                GameObject gameObject = new GameObject();
                gameObject.transform.SetParent(T, false);
                carSound = gameObject.AddComponent<AudioSource>();
                carSound.playOnAwake = false;
                carSound.reverbZoneMix = audioSourceTemplate.reverbZoneMix;
                carSound.spatialBlend = audioSourceTemplate.spatialBlend;
                carSound.dopplerLevel = audioSourceTemplate.dopplerLevel;
                carSound.spread = audioSourceTemplate.spread;
                carSound.rolloffMode = audioSourceTemplate.rolloffMode;
                carSound.minDistance = audioSourceTemplate.minDistance;
                carSound.maxDistance = audioSourceTemplate.maxDistance;
                gameObject.transform.parent = this.gameObject.transform;
                gameObject.transform.localPosition = Vector3.zero;
                carSound.transform.parent = T;
                carSound.volume = 0;
                carSound.loop = true;
                carSound.clip = sound.audioClip;

                if (mixer)
                    carSound.outputAudioMixerGroup = mixer;

                _accelerateAudios.Add(carSound);
                AddAudioEffects(carSound);
            }
            foreach (var sound in deceleratingSounds)
            {
                GameObject gameObject = new GameObject();
                gameObject.transform.SetParent(T, false);
                carSound = gameObject.AddComponent<AudioSource>();
                carSound.playOnAwake = false;
                carSound.reverbZoneMix = audioSourceTemplate.reverbZoneMix;
                carSound.spatialBlend = audioSourceTemplate.spatialBlend;
                carSound.dopplerLevel = audioSourceTemplate.dopplerLevel;
                carSound.spread = audioSourceTemplate.spread;
                carSound.rolloffMode = audioSourceTemplate.rolloffMode;
                carSound.minDistance = audioSourceTemplate.minDistance;
                carSound.maxDistance = audioSourceTemplate.maxDistance;
                gameObject.transform.parent = this.gameObject.transform;
                gameObject.transform.localPosition = Vector3.zero;
                carSound.transform.parent = T;
                carSound.volume = 0;
                carSound.loop = true;
                carSound.clip = sound.audioClip;

                if (mixer)
                    carSound.outputAudioMixerGroup = mixer;

                _decelerateAudios.Add(carSound);
                AddAudioEffects(carSound);
            }

            InitializeAudioFilters();
            InitializeBurbleAudioSources();
            InitializeLuggingAudioSources();
        }

        private void InitializeBurbleAudioSources()
        {
            burbleAudioSources = new List<AudioSource>();
            Transform burbleContainer = new GameObject("Burble Sources").transform;
            burbleContainer.SetParent(transform, false);

            for (int i = 0; i < BURBLE_AUDIO_POOL_SIZE; i++)
            {
                GameObject burbleObj = new GameObject($"Burble Source {i}");
                burbleObj.transform.SetParent(burbleContainer, false);
                AudioSource burbleSource = burbleObj.AddComponent<AudioSource>();

                burbleSource.playOnAwake = false;
                burbleSource.loop = false;
                burbleSource.priority = 136;
                burbleSource.spatialBlend = audioSourceTemplate.spatialBlend;
                burbleSource.dopplerLevel = audioSourceTemplate.dopplerLevel;
                burbleSource.spread = audioSourceTemplate.spread;
                burbleSource.rolloffMode = audioSourceTemplate.rolloffMode;
                burbleSource.minDistance = 5f;
                burbleSource.maxDistance = 500f;

                if (mixer)
                    burbleSource.outputAudioMixerGroup = mixer;

                burbleAudioSources.Add(burbleSource);
            }
        }

        private void InitializeLuggingAudioSources()
        {
            luggingAudioSources = new List<AudioSource>();
            if (luggingSounds == null || luggingSounds.Length == 0) return;

            Transform luggingContainer = new GameObject("Lugging Sources").transform;
            luggingContainer.SetParent(transform, false);

            for (int i = 0; i < LUGGING_AUDIO_POOL_SIZE; i++)
            {
                GameObject luggingObj = new GameObject($"Lugging Source {i}");
                luggingObj.transform.SetParent(luggingContainer, false);
                AudioSource luggingSource = luggingObj.AddComponent<AudioSource>();

                luggingSource.playOnAwake = false;
                luggingSource.loop = true;
                luggingSource.priority = 130;
                luggingSource.spatialBlend = audioSourceTemplate.spatialBlend;
                luggingSource.dopplerLevel = audioSourceTemplate.dopplerLevel;
                luggingSource.spread = audioSourceTemplate.spread;
                luggingSource.rolloffMode = audioSourceTemplate.rolloffMode;
                luggingSource.minDistance = audioSourceTemplate.minDistance;
                luggingSource.maxDistance = audioSourceTemplate.maxDistance;
                luggingSource.volume = 0;

                if (mixer)
                    luggingSource.outputAudioMixerGroup = mixer;

                luggingAudioSources.Add(luggingSource);
            }
        }


        private void AddAudioEffects(AudioSource source)
        {
            AudioLowPassFilter lowPass = source.gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 22000f;

            AudioDistortionFilter distortion = source.gameObject.AddComponent<AudioDistortionFilter>();
            distortion.distortionLevel = 0f;
        }

        internal void TurnOn()
        {
            _isOn = true;
        }

        internal void TurnOff()
        {
            _isOn = false;
            if (_accelerateAudios != null)
                foreach (var source in _accelerateAudios) source.volume = 0;
            if (_decelerateAudios != null)
                foreach (var source in _decelerateAudios) source.volume = 0;
            if (burbleAudioSources != null)
                foreach (var source in burbleAudioSources) source.Stop();
            if (luggingAudioSources != null)
            {
                foreach (var source in luggingAudioSources)
                {
                    source.volume = 0;
                    source.Stop();
                }
            }
            currentLuggingEffectVolume = 0f;
        }

        void CalcVolPitchAcDc(float load, bool acDc)
        {
            if (_accelerateAudios == null)
                return;

            float normalizedRPM = Mathf.InverseLerp(_idleRpm, _maxRpm, _rpm);
            float pitchMultiplier = pitchCurve.Evaluate(normalizedRPM);

            _finalPitch = _rpm > _idleRpm + 75 ? (load * loadEffectivenessOnPitch) + pitchMultiplier : idlePitch;

            float volumeMultiplier = volumeCurve.Evaluate(normalizedRPM);

            if (launchMode || _nonDecelerateAudiosMode)
            {
                _finalAccVol = 1.0f;
                _finalDecVol = 0.0f;
            }
            else
            {
                float blendT = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(
                        loadCrossoverPoint - loadBlendWidth * 0.5f,
                        loadCrossoverPoint + loadBlendWidth * 0.5f,
                        load));

                if ((l_r + 75 < _rpm && autoBlip) || (_rpm <= _idleRpm && l_r + 75 > _rpm))
                    blendT = 1.0f;

                _finalAccVol = blendT;
                _finalDecVol = 1.0f - blendT;
            }

            if (_accelerateAudios.Count == 1)
            {
                if (!_isOn || _rpm < 100)
                {
                    _accelerateAudios[0].pitch = 1;
                    _accelerateAudios[0].volume = 0;
                }
                else
                {
                    if (!_accelerateAudios[0].isPlaying)
                        _accelerateAudios[0].Play();

                    float rawAccVol0 = volumeMultiplier * maxVolumeAcc * masterVolume
                        * Mathf.Lerp(Mathf.Max(load, loadVolumeChangerMinValue), 1, 1 - loadVolumeAccChangerFactor)
                        + _AcvTable[0];

                    if (maxVolumeAcc > 0)
                        _accelerateAudios[0].volume = Mathf.Clamp(rawAccVol0, 0f, maxVolumeAcc);
                    else
                        _accelerateAudios[0].volume = Mathf.Max(0f, rawAccVol0);

                    _accelerateAudios[0].pitch = shiftPitchOsc + _finalPitch + targetedShiftPitch + acPitchTrim + _AcpTable[0];

                    ApplyAudioEffects(0, normalizedRPM, load, isAcc: true);
                }
            }
            else
            {
                for (int i = 0; i < _accelerateAudios.Count; i++)
                {
                    vol = 0.0f;
                    _accelerateAudios[i].mute = true;
                    _accelerateAudios[i].priority = 256;
                    if (_isOn)
                    {
                        if (_rpm >= _AcMin_rTable[i] && _rpm < _AcNormal_rTable[i])
                        {
                            _accelerateAudios[i].priority = 128;
                            _accelerateAudios[i].mute = false;
                            float Range = (_AcNormal_rTable[i] - _AcMin_rTable[i]) / _RangeDivider;
                            float Reduced_r = _rpm - _AcMin_rTable[i];
                            vol = _finalAccVol * (Reduced_r / Range);
                        }
                        if (_rpm >= _AcNormal_rTable[i] && _rpm <= _AcMax_rTable[i])
                        {
                            _accelerateAudios[i].priority = 128;
                            _accelerateAudios[i].mute = false;
                            vol = _finalAccVol;
                        }
                        if (_rpm > _AcMax_rTable[i] && _rpm < (i == _accelerateAudios.Count - 1 ? maximumTheoricalRPM : (_AcMax_rTable[i + 1])))
                        {
                            _accelerateAudios[i].priority = 128;
                            _accelerateAudios[i].mute = false;
                            float Range = (((i == _accelerateAudios.Count - 1 ? maximumTheoricalRPM : _AcMax_rTable[i + 1]) - _AcMax_rTable[i])) / _RangeDivider;
                            float Reduced_r = _rpm - _AcMax_rTable[i];
                            vol = (_finalAccVol) * (1 - Reduced_r / Range);
                        }
                        vol *= Mathf.Lerp(Mathf.Max(load, loadVolumeChangerMinValue), 1, 1 - loadVolumeAccChangerFactor);
                        if (!_accelerateAudios[i].mute)
                        {
                            if (_rpm > 0)
                            {
                                _accelerateAudios[i].pitch = shiftPitchOsc + _finalPitch + targetedShiftPitch + acPitchTrim;

                                float rawAccVol = vol * volumeMultiplier * masterVolume + _AcvTable[i];
                                if (maxVolumeAcc > 0)
                                    _accelerateAudios[i].volume = Mathf.Clamp(rawAccVol, 0f, maxVolumeAcc);
                                else
                                    _accelerateAudios[i].volume = Mathf.Max(0f, rawAccVol);

                                if (!_accelerateAudios[i].isPlaying)
                                    _accelerateAudios[i].Play();

                                _accelerateAudios[i].pitch += _AcpTable[i];
                            }
                            else
                                _accelerateAudios[i].volume = 0;

                            ApplyAudioEffects(i, normalizedRPM, load, isAcc: true);
                        }
                    }
                }
            }
            if (acDc)
            {
                float finalDecPitch = _rpm > _idleRpm + 75
                    ? (load * loadEffectivenessOnPitch) + pitchCurve.Evaluate(normalizedRPM)
                    : idlePitch;

                for (int i = 0; i < _decelerateAudios.Count; i++)
                {
                    vol = 0.0f;
                    _decelerateAudios[i].mute = true;
                    _decelerateAudios[i].priority = 256;
                    if (_isOn)
                    {
                        if (_rpm >= _DcMin_rTable[i] && _rpm < _DcNormal_rTable[i])
                        {
                            _decelerateAudios[i].priority = 128;
                            _decelerateAudios[i].mute = false;
                            float Range = (_DcNormal_rTable[i] - _DcMin_rTable[i]) / _RangeDivider;
                            float Reduced_r = _rpm - _DcMin_rTable[i];
                            vol = _finalDecVol * (Reduced_r / Range);
                        }
                        if (_rpm >= _DcNormal_rTable[i] && _rpm <= _DcMax_rTable[i])
                        {
                            _decelerateAudios[i].priority = 128;
                            _decelerateAudios[i].mute = false;
                            vol = _finalDecVol;
                        }
                        if (_rpm > _DcMax_rTable[i] && _rpm < (i == _decelerateAudios.Count - 1 ? maximumTheoricalRPM : (_DcMax_rTable[i + 1])))
                        {
                            _decelerateAudios[i].priority = 128;
                            _decelerateAudios[i].mute = false;
                            float Range = (((i == _decelerateAudios.Count - 1 ? maximumTheoricalRPM : _DcMax_rTable[i + 1]) - _DcMax_rTable[i])) / _RangeDivider;
                            float Reduced_r = _rpm - _DcMax_rTable[i];
                            vol = (_finalDecVol) * (1 - Reduced_r / Range);
                        }
                        vol *= Mathf.Lerp(Mathf.Max(load, loadVolumeChangerMinValue), 1, 1 - loadVolumeDccChangerFactor);
                        if (!_decelerateAudios[i].mute)
                        {
                            if (_rpm > 150)
                            {
                                _decelerateAudios[i].pitch = shiftPitchOsc + finalDecPitch + targetedShiftPitch + dcPitchTrim;

                                float rawDecVol = vol * volumeMultiplier * masterVolume + _DcvTable[i];
                                if (maxVolumeDcc > 0)
                                    _decelerateAudios[i].volume = Mathf.Clamp(rawDecVol, 0f, maxVolumeDcc);
                                else
                                    _decelerateAudios[i].volume = Mathf.Max(0f, rawDecVol);

                                if (!_decelerateAudios[i].isPlaying)
                                    _decelerateAudios[i].Play();

                                _decelerateAudios[i].pitch += _DcpTable[i];
                            }
                            else
                                _decelerateAudios[i].volume = 0;

                            ApplyAudioEffects(i, normalizedRPM, load, isAcc: false);
                        }
                    }
                }
            }
            l_r = _rpm;
        }

        IEnumerator CalculateAsync()
        {
            while (true)
            {
                if (_debug)
                {
                    _load = debug_load;
                    _rpm = debug_rpm;
                }
                else
                {
                    _load = load;
                    _rpm = rpm;
                }

                if (!_nonDecelerateAudiosMode)
                {
                    CalcVolPitchAcDc(_load, true);
                }
                else
                {
                    CalcVolPitchAcDc(_load, false);
                }

                HandleExhaustBurble();
                HandleEngineLugging();

                if (enableDiagnosticLogger && Time.time >= _nextLogTime)
                {
                    LogDiagnostics();
                    _nextLogTime = Time.time + 1f;
                }

                yield return new WaitForFixedUpdate();
            }
        }

        [Header("Burble Diagnostics")]
        [Tooltip("Enable per-tick burble gate logging (disable in production)")]
        public bool enableBurbleDiagnostics = false;
        private float _burbleDiagNextLog = 0f;

        private void HandleExhaustBurble()
        {
            if (!enableExhaustBurble || burbleSounds == null || burbleSounds.Length == 0)
                return;

            float loadDelta = lastLoad - _load;
            float rpmDrop = _lastBurbleRPM - _rpm;
            float currentTime = Time.time;

            bool rpmOk = _rpm >= burbleMinRPM;
            bool deltaOk = loadDelta >= burbleLoadThreshold;
            bool rpmDropOk = burbleRPMDropThreshold > 0f && rpmDrop >= burbleRPMDropThreshold;
            bool cooldownOk = currentTime - lastBurbleTime >= minBurbleDelay;
            bool canBurble = rpmOk && (deltaOk || rpmDropOk) && cooldownOk && _isOn;

            string trigger = deltaOk ? "load-delta" : rpmDropOk ? "rpm-drop" : "none";

            if (enableBurbleDiagnostics && rpmOk && currentTime >= _burbleDiagNextLog)
            {
                _burbleDiagNextLog = currentTime + 0.1f;
                string block = canBurble
                    ? $"FIRE ({trigger})"
                    : (!deltaOk && !rpmDropOk
                        ? $"BLOCKED  loadDelta={loadDelta:F4}(need>={burbleLoadThreshold:F2})  rpmDelta={rpmDrop:F0}(need>={burbleRPMDropThreshold:F0})"
                        : !cooldownOk
                            ? $"BLOCKED_cooldown({(currentTime - lastBurbleTime):F2}s < {minBurbleDelay:F2}s)"
                            : "BLOCKED_other");
                Debug.Log($"[Burble] RPM={_rpm:F0}  _load={_load:F4}  lastLoad={lastLoad:F4}  " +
                          $"rawLoad(adapter)={load:F4}  loadDelta={loadDelta:F4}  rpmDelta={rpmDrop:F0}  trigger={block}");
            }

            if (canBurble && Random.value <= burbleProbability)
            {
                AudioSource burbleSource = burbleAudioSources.FirstOrDefault(s => !s.isPlaying);
                if (burbleSource != null)
                {
                    burbleSource.clip = burbleSounds[Random.Range(0, burbleSounds.Length)];

                    float rpmFactor = Mathf.InverseLerp(burbleMinRPM, _maxRpm, _rpm);
                    float triggerFactor = deltaOk
                        ? Mathf.Clamp01(loadDelta / Mathf.Max(burbleLoadThreshold, 0.001f))
                        : Mathf.Clamp01(rpmDrop / Mathf.Max(burbleRPMDropThreshold, 1f));

                    burbleSource.volume = burbleVolume *
                                         Mathf.Lerp(0.5f, 1.0f, rpmFactor) *
                                         Mathf.Lerp(0.7f, 1.0f, triggerFactor) *
                                         masterVolume;

                    float rndBurble = burbleRandomPitchVariation > 0f
                        ? Random.Range(-burbleRandomPitchVariation, burbleRandomPitchVariation)
                        : 0f;
                    burbleSource.pitch = Mathf.Lerp(0.9f, 1.1f, rpmFactor) + rndBurble;

                    burbleSource.Play();
                    lastBurbleTime = currentTime + Random.Range(minBurbleDelay, maxBurbleDelay);

                    if (enableBurbleDiagnostics)
                        Debug.Log($"[Burble] PLAYED  RPM={_rpm:F0}  vol={burbleSource.volume:F3}  pitch={burbleSource.pitch:F3}  trigger={trigger}");
                }
            }

            lastLoad = _load;
            _lastBurbleRPM = _rpm;
        }

        private void HandleEngineLugging()
        {
            if (!enableEngineLugging || luggingSounds == null || luggingSounds.Length == 0 || luggingAudioSources == null || luggingAudioSources.Count == 0)
            {
                if (currentLuggingEffectVolume > 0 && luggingAudioSources != null)
                {
                    currentLuggingEffectVolume = Mathf.Lerp(currentLuggingEffectVolume, 0f, Time.deltaTime * luggingFadeOutSpeed);
                    foreach (var source in luggingAudioSources)
                    {
                        source.volume = currentLuggingEffectVolume * masterVolume;
                        if (source.volume < 0.01f && source.isPlaying)
                        {
                            source.Stop();
                            source.clip = null;
                        }
                    }
                }
                return;
            }

            bool isLugging = _isOn &&
                             _rpm < luggingMaxRPMThreshold &&
                             _rpm > luggingMinRPMThreshold &&
                             _load >= luggingMinLoadThreshold;

            float targetVolume = isLugging ? luggingVolume : 0f;
            float fadeSpeed = isLugging ? luggingFadeInSpeed : luggingFadeOutSpeed;

            currentLuggingEffectVolume = Mathf.Lerp(currentLuggingEffectVolume, targetVolume, Time.deltaTime * fadeSpeed);

            AudioSource activeLuggingSource = luggingAudioSources[0];

            if (isLugging)
            {
                if (!activeLuggingSource.isPlaying && luggingSounds.Length > 0)
                {
                    activeLuggingSource.clip = luggingSounds[Random.Range(0, luggingSounds.Length)];
                    float rndLugging = luggingRandomPitchVariation > 0f
                        ? Random.Range(-luggingRandomPitchVariation, luggingRandomPitchVariation)
                        : 0f;
                    activeLuggingSource.pitch = luggingBasePitch + rndLugging;
                    activeLuggingSource.Play();
                }

                activeLuggingSource.volume = currentLuggingEffectVolume * masterVolume;
            }
            else
            {
                activeLuggingSource.volume = currentLuggingEffectVolume * masterVolume;
                if (activeLuggingSource.volume < 0.01f && activeLuggingSource.isPlaying)
                {
                    activeLuggingSource.Stop();
                    activeLuggingSource.clip = null;
                }
            }
        }


        private void LogDiagnostics()
        {
            float normRPM = Mathf.InverseLerp(_idleRpm, _maxRpm, _rpm);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[VNS Diagnostic] [{mixerType}]  t={Time.time:F1}s");
            sb.AppendLine($"  Engine : RPM={_rpm:F0}  normRPM={normRPM:F3}  Load={_load:F3}  isOn={_isOn}");
            sb.AppendLine($"  AccDecBlend : loadCrossoverPoint={loadCrossoverPoint:F3}  loadBlendWidth={loadBlendWidth:F3}  finalAccVol={_finalAccVol:F3}  finalDecVol={_finalDecVol:F3}");
            sb.AppendLine($"  Strengths (raw/clamped) : LP={lowPassStrength:F2}/{Mathf.Clamp01(lowPassStrength):F2}  HP={highPassStrength:F2}/{Mathf.Clamp01(highPassStrength):F2}  Res={resonanceStrength:F2}/{Mathf.Clamp01(resonanceStrength):F2}  Dist={distortionStrength:F2}/{Mathf.Clamp01(distortionStrength):F2}  Chorus={chorusStrength:F2}/{Mathf.Clamp01(chorusStrength):F2}  Reverb={reverbStrength:F2}/{Mathf.Clamp01(reverbStrength):F2}");
            sb.AppendLine($"  VNS params: mufflingIntensity={mufflingIntensity:F2}  distortionIntensity={distortionIntensity:F2}  lowPassIntensity={lowPassIntensity:F2}");

            if (_accelerateAudios != null)
            {
                for (int i = 0; i < _accelerateAudios.Count; i++)
                {
                    var src = _accelerateAudios[i];
                    if (src == null) continue;

                    var lp = src.GetComponent<AudioLowPassFilter>();
                    var hp = src.GetComponent<AudioHighPassFilter>();
                    var dst = src.GetComponent<AudioDistortionFilter>();
                    var cho = src.GetComponent<AudioChorusFilter>();

                    sb.AppendLine($"  [Acc#{i}] vol={src.volume:F4}  pitch={src.pitch:F3}  muted={src.mute}  playing={src.isPlaying}");
                    sb.AppendLine($"          LP_cutoff={(lp != null ? lp.cutoffFrequency.ToString("F0") : "N/A")} Hz  LP_Q={(lp != null ? lp.lowpassResonanceQ.ToString("F2") : "N/A")}");
                    sb.AppendLine($"          HP_cutoff={(hp != null ? hp.cutoffFrequency.ToString("F0") : "N/A")} Hz");
                    sb.AppendLine($"          Distortion={(dst != null ? dst.distortionLevel.ToString("F3") : "N/A")}");
                    sb.AppendLine($"          ChorusDepth={(cho != null ? cho.depth.ToString("F3") : "N/A")}  ChorusRate={(cho != null ? cho.rate.ToString("F3") : "N/A")}  wet1={(cho != null ? cho.wetMix1.ToString("F3") : "N/A")}");
                    sb.AppendLine($"          reverbZoneMix={src.reverbZoneMix:F3}");
                }
            }

            if (_decelerateAudios != null)
            {
                for (int i = 0; i < _decelerateAudios.Count; i++)
                {
                    var src = _decelerateAudios[i];
                    if (src == null) continue;

                    var lp = src.GetComponent<AudioLowPassFilter>();
                    var hp = src.GetComponent<AudioHighPassFilter>();
                    var dst = src.GetComponent<AudioDistortionFilter>();
                    var cho = src.GetComponent<AudioChorusFilter>();

                    sb.AppendLine($"  [Dec#{i}] vol={src.volume:F4}  pitch={src.pitch:F3}  muted={src.mute}  playing={src.isPlaying}");
                    sb.AppendLine($"          LP_cutoff={(lp != null ? lp.cutoffFrequency.ToString("F0") : "N/A")} Hz  LP_Q={(lp != null ? lp.lowpassResonanceQ.ToString("F2") : "N/A")}");
                    sb.AppendLine($"          HP_cutoff={(hp != null ? hp.cutoffFrequency.ToString("F0") : "N/A")} Hz");
                    sb.AppendLine($"          Distortion={(dst != null ? dst.distortionLevel.ToString("F3") : "N/A")}");
                    sb.AppendLine($"          ChorusDepth={(cho != null ? cho.depth.ToString("F3") : "N/A")}  ChorusRate={(cho != null ? cho.rate.ToString("F3") : "N/A")}  wet1={(cho != null ? cho.wetMix1.ToString("F3") : "N/A")}");
                    sb.AppendLine($"          reverbZoneMix={src.reverbZoneMix:F3}");
                }
            }

            Debug.Log(sb.ToString());
        }

        private void ApplyAudioEffects(int index, float normalizedRPM, float load, bool isAcc)
        {
            if (_accelerateAudios == null) return;

            int arrIdx = isAcc ? index : index + _accelerateAudios.Count;

            if (lowPassFilters == null || arrIdx >= lowPassFilters.Length) return;

            AudioLowPassFilter lowPass = lowPassFilters[arrIdx];
            AudioHighPassFilter highPass = highPassFilters[arrIdx];
            AudioDistortionFilter dist = distortionFilters[arrIdx];
            AudioChorusFilter chorus = flangerFilters[arrIdx];

            AudioSource src = isAcc ? _accelerateAudios[index] : _decelerateAudios[index];

            float speed = Time.deltaTime * 16f;

            float lpStr = Mathf.Clamp01(lowPassStrength);
            float hpStr = Mathf.Clamp01(highPassStrength);
            float resStr = Mathf.Clamp01(resonanceStrength);
            float dstStr = Mathf.Clamp01(distortionStrength);
            float choStr = Mathf.Clamp01(chorusStrength);
            float revStr = Mathf.Clamp01(reverbStrength);

            if (lowPass != null)
            {
                float curveCutoff = lowPassCurve.Evaluate(load);
                float baseCutoff = Mathf.Lerp(22000f, curveCutoff, lowPassIntensity);
                float boostCutoff = Mathf.Lerp(baseCutoff, curveCutoff * mufflingIntensity, lpStr);
                lowPass.cutoffFrequency = Mathf.Lerp(lowPass.cutoffFrequency,
                    Mathf.Max(boostCutoff, 10f), speed);

                float qPeak = Mathf.Sin(normalizedRPM * Mathf.PI);
                lowPass.lowpassResonanceQ = Mathf.Lerp(1f, 1f + 7f * resStr, qPeak);
            }

            if (highPass != null)
            {
                float hpCutoff = Mathf.Lerp(10f, 4000f,
                    hpStr * normalizedRPM * normalizedRPM);
                highPass.cutoffFrequency = Mathf.Lerp(highPass.cutoffFrequency, hpCutoff, speed);
            }

            if (dist != null)
            {
                float factor = (normalizedRPM + load) * 0.5f;
                float baseDistortion = distortionCurve.Evaluate(factor) * distortionIntensity;
                float boostedDistortion = baseDistortion * (1f + dstStr);
                dist.distortionLevel = Mathf.Clamp01(boostedDistortion);
            }

            if (chorus != null)
            {
                float chorusActive = Mathf.Clamp01((normalizedRPM - 0.3f) / 0.7f);
                float effectiveChorus = choStr * chorusActive;
                chorus.depth = effectiveChorus * 0.5f;
                chorus.rate = 0.2f + normalizedRPM * 0.5f;
                chorus.dryMix = 1f;
                chorus.wetMix1 = effectiveChorus * 0.5f;
                chorus.wetMix2 = effectiveChorus * 0.5f;
                chorus.wetMix3 = effectiveChorus * 0.5f;
            }

            if (src != null)
            {
                float templateReverb = audioSourceTemplate != null ? audioSourceTemplate.reverbZoneMix : 1f;
                float reverbTarget = Mathf.Lerp(templateReverb, 1.1f, revStr * normalizedRPM * load);
                src.reverbZoneMix = Mathf.Lerp(src.reverbZoneMix, reverbTarget, Time.deltaTime * 5f);
            }
        }


        private void OnEnable()
        {
            StartCoroutine(CalculateAsync());
        }

        private void OnDisable()
        {
            TurnOff();
            StopCoroutine(CalculateAsync());
        }


        private void InitializeAudioFilters()
        {
            int totalAudioSources = _accelerateAudios.Count + _decelerateAudios.Count;
            lowPassFilters = new AudioLowPassFilter[totalAudioSources];
            highPassFilters = new AudioHighPassFilter[totalAudioSources];
            distortionFilters = new AudioDistortionFilter[totalAudioSources];
            flangerFilters = new AudioChorusFilter[totalAudioSources];

            for (int i = 0; i < _accelerateAudios.Count; i++)
            {
                GameObject audioObj = _accelerateAudios[i].gameObject;
                InitializeFiltersForAudioSource(audioObj, i);
            }

            for (int i = 0; i < _decelerateAudios.Count; i++)
            {
                int index = i + _accelerateAudios.Count;
                GameObject audioObj = _decelerateAudios[i].gameObject;
                InitializeFiltersForAudioSource(audioObj, index);
            }
        }

        private void InitializeFiltersForAudioSource(GameObject audioObj, int index)
        {
            lowPassFilters[index] = GetOrAddComponent<AudioLowPassFilter>(audioObj);
            lowPassFilters[index].cutoffFrequency = 22000f;
            lowPassFilters[index].lowpassResonanceQ = 1f;
            highPassFilters[index] = GetOrAddComponent<AudioHighPassFilter>(audioObj);
            highPassFilters[index].cutoffFrequency = 10f;
            distortionFilters[index] = GetOrAddComponent<AudioDistortionFilter>(audioObj);
            distortionFilters[index].distortionLevel = 0f;
            flangerFilters[index] = GetOrAddComponent<AudioChorusFilter>(audioObj);
            flangerFilters[index].depth = 0f;
            flangerFilters[index].rate = 0.1f;
            flangerFilters[index].dryMix = 1f;
            flangerFilters[index].wetMix1 = 0f;
            flangerFilters[index].wetMix2 = 0f;
            flangerFilters[index].wetMix3 = 0f;
        }

        private T GetOrAddComponent<T>(GameObject obj) where T : Component
        {
            T component = obj.GetComponent<T>();
            if (component == null)
            {
                component = obj.AddComponent<T>();
            }
            return component;
        }
    }
}
