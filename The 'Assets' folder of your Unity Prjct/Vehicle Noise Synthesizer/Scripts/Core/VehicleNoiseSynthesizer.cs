/*
Several GitHub repositories inspire this but I lost their URLs - Please contact me if you're one of them in case of needing to add your personal website or username as a credit.
Written by ImDanOush (find me with this username @ImDanOush on IG, YT, TWTR,...) for "ATG Life Style and Vehicle Simulator" (@ATG_Simulator)
This entirely is freeware, See the repository's license section for more info.
*/

using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Audio;
using System;
using System.Collections;
using static Unity.VisualScripting.Member;

namespace AroundTheGroundSimulator
{
    public class VehicleNoiseSynthesizer : MonoBehaviour
    {
        // Control input manually
        public bool _debug = false;
        [Range(100f, 9000f)]
        public float debug_rpm = 800;
        [Range(0.00f, 1.00f)]
        public float debug_load = 1;

        [Space(7f)]
        [Header("Granulator Properties")]
        [Space(7f)]
        // Shift final Pitch for fine-tuning (Optional)
        public float shiftPitch = 0;
        // Change final volume for fine-tuning (Optional)
        [Range(0.007f, 1.00f)]
        public float masterVolume = 1;

        [Header("Engine Sound Curves")]
        public AnimationCurve pitchCurve = new AnimationCurve(
            new Keyframe(0f, 0.6f),
            new Keyframe(1f, 1.2f)
        );

        public AnimationCurve volumeCurve = new AnimationCurve(
            new Keyframe(0f, 0.5f),
            new Keyframe(1f, 1f)
        );

        public AudioSource audioSourceTemplate;
        public AudioMixerGroup mixer; // Audio Mixer Group assigned to the audio sources of this script - optional
        public MixerType mixerType;   // Which one of the three default types should be used? (see the audio mixer in the demo, there are Engine, Intake and Exhaust ones by default)

        public int rpm_deviation = 1000; // the constant difference of the rpm value of audio clips, e.g audio clip 1 has the audio of an engine at 1000 rpm, audio clip 2 has  has the audio of an engine at 2500 rpm. so the value should be close or a bit less than 1500

        //

        [Header("Audio Effect Curves")]
        public AnimationCurve distortionCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.7f, 0.3f),
            new Keyframe(1f, 0.5f)
        );

        [Header("Audio Effect Parameters")]
        [Range(0f, 1f)]
        public float distortionIntensity = 0.5f;
        [Range(0f, 1f)]
        public float mufflingIntensity = 0.5f;

        [Header("Low Pass Filter")]
        public AnimationCurve lowPassCurve = new AnimationCurve(
            new Keyframe(1f, 1000f),  // Heavy filtering
            new Keyframe(0f, 22000f)  // Full frequency range
        );
        [Range(0f, 1f)]
        public float lowPassIntensity = 0.5f;

        private AudioLowPassFilter[] acceleratingLowPass;
        private AudioLowPassFilter[] deceleratingLowPass;
        private AudioDistortionFilter[] acceleratingDistortion;
        private AudioDistortionFilter[] deceleratingDistortion;

        //

        [System.Serializable]
        public class EngineAudioClipData
        {
            public AudioClip audioClip;
            public int rpmValue;
        }

        [Tooltip("Audio clips for engine acceleration with their respective RPM values")]
        public List<EngineAudioClipData> acceleratingSounds = new List<EngineAudioClipData>();
        [Tooltip("[Optional] Audio clips for engine deceleration with their respective RPM values")]
        public List<EngineAudioClipData> deceleratingSounds = new List<EngineAudioClipData>();

        // Mimimum and Maximum values of volumes
        [Tooltip("If there is no decelrating sound clips, the accelerating sound clip with the lowest RPM value is chosen and this will be its default volume when it is idle.")]
        [Range(0.05f, 1.00f)]
        public float idleAccVolume = 0.1f;
        [Tooltip("If max volume is set to 0 that means the audio volum is left as is.")]
        [Range(0.00f, 1.00f)]
        public float maxVolumeAcc = 0.4f;
        [Range(0.00f, 1.00f)]
        public float maxVolumeDcc = 0.1f;

        [Space(7f)]
        [Header("Advanced Granulator Properties")]
        [Space(7f)]
        // How smooth audioclips and their volume value can change?
        public float transitionTime = 20f;
        // How smooth accelerating audioclips and their pitch value can change? suggested to use the same value as transitionTime.
        public float acPitchTransitionTime = 20f;
        // How smooth decelerating audioclips and their pitch value can change? suggested to use the same value as transitionTime.
        public float dcPitchTransitionTime = 20f;
        // Adjust the pitch of accelerating sound clip for fine-tuning
        public float acPitchTrim = 0;
        // Adjust the pitch of accelerating sound clip for fine-tuning
        public float dcPitchTrim = 0;
        // Add random pitch
        [Range(-0.060f, 0.060f)]
        public float rndmPitch = 0;
        // Used for the table of pitches, if the maximum RPM of your engine is more than 10000 set it something more than that.
        public float maximumTheoricalRPM = 10000;
        public float idlePitch = 1;

        //[HideInInspector]
        public float rpm
        {
            get; internal set;  // sets the rpm value based on one of the inputs like the provided "AudioGranulatorNWHVehiclePhysics2" class
        }
        //[HideInInspector]
        public float load
        {
            get; internal set;  // sets the load value based on one of the inputs like the provided "AudioGranulatorNWHVehiclePhysics2" class
        }

        [Space(47f)]
        [Header("[Read-Only] Debug Values")]
        [Space(7f)]
        // Table of audio clip ranges for fading based on the rpm value
        [SerializeField]
        float[] _AcMin_rTable;
        [SerializeField]
        float[] _AcNormal_rTable;
        [SerializeField]
        float[] _AcMax_rTable;
        [SerializeField]
        float[] _DcMin_rTable;
        [SerializeField]
        float[] _DcNormal_rTable;
        [SerializeField]
        float[] _DcMax_rTable;
        [SerializeField]
        bool _nonDecelerateAudiosMode = true;
        [SerializeField]
        float _rpm; // current engine rpm
        [SerializeField]
        float _load; // current engine load
        [SerializeField]
        float _maxRpm; // max rpm of the engine
        [SerializeField]
        float _RangeDivider = 1; // scale the range of which an audio clip fades out/in to another one per its audio clip type - accelerating/decelerating

        // used for calculations
        bool _isOn = false; //is the car turned on?
        float _idleRpm; // used for correcting idle audio volume level
        float l_r; // the rpm value in previous frame
        float _finalAccVol = 0; // values of audio clips when the vehicle is accelerating
        float _finalDecVol = 0; // values of audio clips wheb the vehicle is decelerating
        [SerializeField] float _finalPitch = 1f; // final raw pitch value
        private List<AudioSource> _accelerateAudios;
        private List<AudioSource> _decelerateAudios;
        float vol;

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