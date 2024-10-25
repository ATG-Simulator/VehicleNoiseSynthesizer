using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Audio;
using System.Collections;

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
        #endregion

        #region Core Audio Settings
        [Header("Core Audio Settings")]
        [Space(10)]
        [Tooltip("Fine-tune the overall pitch of engine sounds")]
        public float shiftPitch = 0;

        [Range(0.007f, 1.00f)]
        [Tooltip("Master volume control for all engine sounds")]
        public float masterVolume = 1;

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

            [Tooltip("Optional description for this audio clip")]
            public string description;
        }
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
        [Tooltip("Default volume when engine is idle (used when no deceleration clips are present)")]
        [Range(0.05f, 1.00f)]
        public float idleAccVolume = 0.1f;

        [Tooltip("Maximum volume for acceleration sounds (0 means no limit)")]
        [Range(0.00f, 1.00f)]
        public float maxVolumeAcc = 0.4f;

        [Tooltip("Maximum volume for deceleration sounds")]
        [Range(0.00f, 1.00f)]
        public float maxVolumeDcc = 0.1f;

        [Header("Transition Settings")]
        [Space(10)]
        [Tooltip("How smoothly audio clips and volume changes occur")]
        [Range(1f, 50f)]
        public float transitionTime = 20f;

        [Tooltip("Smoothness of pitch changes during acceleration")]
        [Range(1f, 50f)]
        public float acPitchTransitionTime = 20f;

        [Tooltip("Smoothness of pitch changes during deceleration")]
        [Range(1f, 50f)]
        public float dcPitchTransitionTime = 20f;
        #endregion

        #region Fine-Tuning Parameters
        [Header("Fine-Tuning")]
        [Space(10)]
        [Tooltip("Fine-tune pitch for acceleration sounds")]
        [Range(-1f, 1f)]
        public float acPitchTrim = 0;

        [Tooltip("Fine-tune pitch for deceleration sounds")]
        [Range(-1f, 1f)]
        public float dcPitchTrim = 0;

        [Tooltip("Add random variation to pitch")]
        [Range(-0.060f, 0.060f)]
        public float rndmPitch = 0;

        [Tooltip("Maximum theoretical RPM value for calculations")]
        [Range(1000f, 20000f)]
        public float maximumTheoricalRPM = 10000;

        [Tooltip("Base pitch value when engine is idle")]
        [Range(0.5f, 2f)]
        public float idlePitch = 1;
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
        private float vol;
        #endregion

        #region Public Properties
        public float rpm { get; internal set; }
        public float load { get; internal set; }
        #endregion

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
            //max rpm of the engine
            _maxRpm = maxRpm;
            _idleRpm = idleRpm;


            // one-time process to prepare and load audio clips
            _nonDecelerateAudiosMode = true;
            int counter = 0;
            _AcNormal_rTable = new float[acceleratingSounds.Count];
            foreach (EngineAudioClipData item in acceleratingSounds)
            {
                _AcNormal_rTable[counter] = item.rpmValue;
                counter++;
            }
            counter = 0;
            _DcNormal_rTable = new float[deceleratingSounds.Count];
            foreach (EngineAudioClipData item in deceleratingSounds)
            {
                _DcNormal_rTable[counter] = item.rpmValue;
                counter++;
            }
            // if no decelerating audio clips are provided - like for vehicle engine and vehicle engine intake sounds - then this changes the script behavior accordingly
            if (counter != 0)
                _nonDecelerateAudiosMode = false;

            //Auto setup Min & Max _r tables
            _AcMax_rTable = new float[_AcNormal_rTable.Length];
            _AcMin_rTable = new float[_AcNormal_rTable.Length];

            for (int i = 1; i < _AcNormal_rTable.Length; i++)
            {
                _AcMax_rTable[i] = _AcNormal_rTable[i] + rpm_deviation;
                _AcMin_rTable[i] = _AcNormal_rTable[i] - rpm_deviation;
            }
            _AcMin_rTable[0] = 0;

            _DcMax_rTable = new float[_DcNormal_rTable.Length];
            _DcMin_rTable = new float[_DcNormal_rTable.Length];

            for (int i = 1; i < _DcNormal_rTable.Length; i++)
            {
                _DcMax_rTable[i] = _DcNormal_rTable[i] + rpm_deviation;
                _DcMin_rTable[i] = _DcNormal_rTable[i] - rpm_deviation;

                _DcMin_rTable[0] = 0;
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

            // Sets up audio sources for accelerating and decelerating sounds with some default values. Change the audio source properties here down below to your likings.
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
        }

        private void AddAudioEffects(AudioSource source)
        {
            // Add Low Pass Filter
            AudioLowPassFilter lowPass = source.gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 22000f; // Start with no filtering

            // Add Distortion
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
        }

        void CalcVolPitchAcDc(float load, bool acDc)
        {
            if (_accelerateAudios == null) // If audio sources are not ready yet, ignore doing this function.
                return;

            float normalizedRPM = Mathf.InverseLerp(_idleRpm, _maxRpm, _rpm);
            float pitchMultiplier = pitchCurve.Evaluate(normalizedRPM);
            _finalPitch = Mathf.Lerp(_finalPitch, (_rpm > _idleRpm + rpm_deviation ? pitchMultiplier : 1f), Time.deltaTime * transitionTime);

            if (l_r + 75 < _rpm || _nonDecelerateAudiosMode || load > 0f)
            {
                _finalAccVol = Mathf.Lerp(_finalAccVol, 1.0f, Time.deltaTime * transitionTime);
                _finalDecVol = Mathf.Lerp(_finalDecVol, 0.0f, Time.deltaTime * transitionTime);
            }
            else
            {
                _finalAccVol = Mathf.Lerp(_finalAccVol, 0.0f, Time.deltaTime * transitionTime);
                _finalDecVol = Mathf.Lerp(_finalDecVol, 1.0f, Time.deltaTime * transitionTime);
            }

            if (_accelerateAudios.Count == 1) // Calculation for when only one audio clip is used
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

                    float volumeMultiplier = volumeCurve.Evaluate(normalizedRPM);
                    if (maxVolumeAcc > 0)
                        _accelerateAudios[0].volume = volumeMultiplier * Mathf.Clamp(volumeMultiplier + idleAccVolume, idleAccVolume, maxVolumeAcc) * masterVolume;
                    else
                        _accelerateAudios[0].volume = volumeMultiplier * masterVolume;

                    _accelerateAudios[0].pitch = Mathf.Lerp(_accelerateAudios[0].pitch, _finalPitch + shiftPitch + acPitchTrim, Time.deltaTime * (acPitchTransitionTime)) + rndmPitch;
                    ApplyAudioEffects(_accelerateAudios[0], normalizedRPM, load);
                }
            }
            else // Calculation for when either both audio clip types are used or more than one accelerating sound clip is used
            {
                for (int i = 0; i < _accelerateAudios.Count; i++) // Calculation for accelerating audio clips
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
                            float Range = _AcNormal_rTable[i] - _AcMin_rTable[i] / _RangeDivider;
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
                        if (!_accelerateAudios[i].mute)
                        {
                            if (_rpm > 0)
                            {
                                _accelerateAudios[i].pitch = Mathf.Lerp(_accelerateAudios[i].pitch, _rpm <= _idleRpm + rpm_deviation ? idlePitch : _finalPitch + shiftPitch + acPitchTrim, Time.deltaTime * (acPitchTransitionTime)) + rndmPitch;

                                float volumeMultiplier = volumeCurve.Evaluate(normalizedRPM);
                                if (maxVolumeAcc > 0)
                                {
                                    _accelerateAudios[i].volume = vol * Mathf.Clamp(volumeMultiplier + idleAccVolume, _decelerateAudios.Count == 0 ? (_rpm <= _idleRpm ? idleAccVolume : 0f) : 0f, maxVolumeAcc) * masterVolume;
                                }
                                else
                                {
                                    _accelerateAudios[i].volume = vol * volumeMultiplier * masterVolume;
                                }
                                if (!_accelerateAudios[i].isPlaying)
                                    _accelerateAudios[i].Play();
                            }
                            else
                                _accelerateAudios[i].volume = 0;
                        }
                        ApplyAudioEffects(_accelerateAudios[i], normalizedRPM, load);
                    }
                }
            }
            if (acDc)  // Calculation for decelerating audio clips if possible
            {
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
                            float Range = _DcNormal_rTable[i] - _DcMin_rTable[i] / _RangeDivider;
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
                        if (!_decelerateAudios[i].mute)
                        {
                            if (_rpm > 0)
                            {
                                _decelerateAudios[i].pitch = Mathf.Lerp(_decelerateAudios[i].pitch, _rpm <= _idleRpm + rpm_deviation ? idlePitch : _finalPitch + shiftPitch + dcPitchTrim, Time.deltaTime * (dcPitchTransitionTime)) + rndmPitch;

                                normalizedRPM = Mathf.InverseLerp(_idleRpm, _maxRpm, _rpm);
                                float volumeMultiplier = volumeCurve.Evaluate(normalizedRPM);

                                if (maxVolumeDcc > 0)
                                {
                                    _decelerateAudios[i].volume = vol * Mathf.Clamp(volumeMultiplier + idleAccVolume, 0, maxVolumeDcc) * masterVolume;
                                }
                                else
                                {
                                    _decelerateAudios[i].volume = vol * volumeMultiplier * masterVolume;
                                }

                                if (!_decelerateAudios[i].isPlaying)
                                    _decelerateAudios[i].Play();
                            }
                            else
                                _decelerateAudios[i].volume = 0;
                        }
                        ApplyAudioEffects(_accelerateAudios[i], normalizedRPM, load);
                    }
                }
            }
            l_r = _rpm;
        }

        IEnumerator CalculateAsync()
        {
            while (true)
            {
                // use debug boolean to manually check the granulator
                if (_debug)
                {
                    _rpm = debug_rpm;
                    _load = debug_load;
                }
                else
                {
                    _rpm = rpm;
                    _load = load;
                }

                if (!_nonDecelerateAudiosMode)
                {
                    CalcVolPitchAcDc(_load, true);
                }
                else
                    CalcVolPitchAcDc(_load, false);

                yield return new WaitForSeconds(Time.fixedDeltaTime);
            }
        }

        private void ApplyAudioEffects(AudioSource source, float normalizedRPM, float load)
        {
            // Get the audio effect components
            AudioLowPassFilter lowPass = source.GetComponent<AudioLowPassFilter>();
            AudioDistortionFilter distortion = source.GetComponent<AudioDistortionFilter>();

            if (lowPass == null || distortion == null)
                return;

            // Calculate combined load/RPM factor for effects
            float loadFactor = Mathf.Lerp(1f - load, 1f, 1f - mufflingIntensity);
            float rpmFactor = normalizedRPM;
            float combinedFactor = (loadFactor + rpmFactor) * 0.5f;

            // Apply low pass filter
            float cutoffFrequency = lowPassCurve.Evaluate(1f - loadFactor) * lowPassIntensity;
            lowPass.cutoffFrequency = cutoffFrequency;

            // Apply distortion
            float distortionAmount = distortionCurve.Evaluate(combinedFactor) * distortionIntensity;
            distortion.distortionLevel = distortionAmount;
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
    }
}