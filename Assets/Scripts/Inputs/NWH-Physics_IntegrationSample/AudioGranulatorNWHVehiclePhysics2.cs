using NWH.VehiclePhysics2;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace AroundTheGroundSimulator
{
    /// <summary>Vehicle Noise Synthesizer v1.9 — NWH Vehicle Physics 2 integration sample with pitch oscillation.</summary>
    [RequireComponent(typeof(VehicleNoiseSynthesizer))]
    public class AudioGranulatorNWHVehiclePhysics2 : MonoBehaviour
    {
        VehicleNoiseSynthesizer aG;

        VehicleController vp;


        public bool enableOscillation = true;

        [SerializeField]
        EnginePitchOscillator pitchOsc = new EnginePitchOscillator();


        void OnEnable()
        {
            aG = GetComponent<VehicleNoiseSynthesizer>();
            vp = this.GetComponentInParent<VehicleController>();

            vp.powertrain.engine.onStart.AddListener(aG.TurnOn);
            vp.powertrain.engine.onStop.AddListener(aG.TurnOff);

            aG.Activate();
        }

        void OnDisable()
        {
            vp.powertrain.engine.onStart.RemoveListener(aG.TurnOn);
            vp.powertrain.engine.onStop.RemoveListener(aG.TurnOff);
        }

        private int lastGear = 0;

        private void FixedUpdate()
        {
            aG.load = vp.powertrain.engine.Load;
            aG.rpm = vp.powertrain.engine.OutputRPM;

            int currentGear = vp.powertrain.transmission.Gear;
            if (currentGear != lastGear)
            {
                pitchOsc.OnGearChange();
                lastGear = currentGear;
            }

            aG.shiftPitchOsc = pitchOsc.ProcessPitch(
                aG.rpm,
                aG.load,
                enableOscillation,
                Time.deltaTime
            );
        }
    }
    [Serializable]
    public class EnginePitchOscillator
    {
        [Header("Core Oscillation")]
        public float oscillationSpeed = 8.0f;
        public float oscillationDepth = 0.15f;

        [Header("RPM Thresholds")]
        public float rpmIncreaseThreshold = 150.0f;
        public float rpmDecreaseThreshold = 80.0f;
        public float revLimiterThreshold = 50.0f;

        [Header("Response Tuning")]
        public float dampingFactor = 5.0f;
        [Range(0.0f, 1.0f)]
        public float loadInfluence = 0.3f;
        public float effectDelay = 0.05f;

        [Header("Advanced Parameters")]
        public float harmonicFrequency = 2.0f;
        public float harmonicAmplitude = 0.3f;
        [Range(0.0f, 1.0f)]
        public float gearChangeIntensityMultiplier = 1.5f;

        [Header("Debug Information")]
        [SerializeField] private float lastRPM = 0.0f;
        [SerializeField] private float oscillationIntensity = 0.0f;
        [SerializeField] private float delayTimer = 0.0f;
        private float phaseOffset = 0.0f;
        private float harmonicPhase = 0.0f;

        public float ProcessPitch(float currentRPM, float engineLoad, bool enableOscillation, float deltaTime)
        {
            if (!enableOscillation)
                return 0;

            float rpmDelta = currentRPM - lastRPM;
            float rpmDeltaAbs = Mathf.Abs(rpmDelta);
            lastRPM = currentRPM;

            if (rpmDeltaAbs > (rpmDelta > 0 ? rpmIncreaseThreshold : rpmDecreaseThreshold))
            {
                delayTimer += deltaTime;
                if (delayTimer >= effectDelay)
                {
                    oscillationIntensity = 1.0f;
                    delayTimer = 0.0f;
                }
            }
            else
            {
                delayTimer = 0.0f;
            }

            bool isNearRevLimiter = rpmDeltaAbs < revLimiterThreshold;
            float revLimiterMultiplier = isNearRevLimiter ? 1.5f : 1.0f;

            if (oscillationIntensity > 0)
            {
                oscillationIntensity *= Mathf.Exp(-dampingFactor * deltaTime);
            }

            float loadFactor = 1.0f + (engineLoad * loadInfluence);

            float baseSpeed = oscillationSpeed * (1.0f + (currentRPM / 1000.0f));
            phaseOffset = UpdatePhase(phaseOffset, baseSpeed * deltaTime);
            harmonicPhase = UpdatePhase(harmonicPhase, baseSpeed * harmonicFrequency * deltaTime);

            float primaryOsc = Mathf.Sin(phaseOffset);
            float harmonicOsc = Mathf.Sin(harmonicPhase) * harmonicAmplitude;
            float combinedOsc = (primaryOsc + harmonicOsc) / (1 + harmonicAmplitude);

            float oscillation = combinedOsc *
                              oscillationDepth *
                              oscillationIntensity *
                              loadFactor *
                              revLimiterMultiplier;

            return oscillation;
        }

        private float UpdatePhase(float phase, float delta)
        {
            phase += delta;
            if (phase > 2 * Mathf.PI)
                phase -= 2 * Mathf.PI;
            return phase;
        }

        public void OnGearChange()
        {
            oscillationIntensity = gearChangeIntensityMultiplier;
        }

        public void Reset()
        {
            oscillationIntensity = 0.0f;
            lastRPM = 0.0f;
            phaseOffset = 0.0f;
            harmonicPhase = 0.0f;
            delayTimer = 0.0f;
        }
    }
}