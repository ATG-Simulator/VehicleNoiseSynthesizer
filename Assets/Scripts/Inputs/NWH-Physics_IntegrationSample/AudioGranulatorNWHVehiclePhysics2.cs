using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace AroundTheGroundSimulator
{
    // NWH Vehicle Physics 2 integration — FMOD-equivalent architecture.
    //
    // Design contract (mirrors confirmed FMOD behaviour):
    //   • Parameters reach VNS via a bounded linear rate-limiter (LinearSeek),
    //     not an exponential moving average.  FMOD Seek Speed is explicitly
    //     documented as linear interpolation, not an EMA / S-curve.
    //     Source: https://qa.fmod.com/t/change-parameter-over-time/11498
    //   • Asymmetric seek speeds (up vs. down) mirror FMOD's per-direction
    //     seek speed toggle present in the parameter deck.
    //   • Rev limiter bypasses seek entirely — equivalent to calling
    //     setParameterByName(..., ignoreseekspeed: true) for sudden events.
    //     Source: https://qa.fmod.com/t/seek-issues-with-parameters/11352
    //   • All signal-shaping logic (coastLoadFloor, mechLoad blend, rev-limiter
    //     ping-pong) is preserved — it is not smoothing, it is physical modelling.
    //   • EnginePitchOscillator is entirely separate (gear-change resonance)
    //     and is not involved in RPM/load seek at all.
    [RequireComponent(typeof(VehicleNoiseSynthesizer))]
    public class AudioGranulatorNWHVehiclePhysics2 : MonoBehaviour
    {
        VehicleNoiseSynthesizer aG;
        VehicleController vp;
        EngineComponent ep;

        public bool enableOscillation = true;

        [SerializeField]
        EnginePitchOscillator pitchOsc = new EnginePitchOscillator();

        // ── Audio Load Mapping ─────────────────────────────────────────────────
        [Header("Audio Load Mapping")]
        [Tooltip("Blend factor for mechanical load into audio signal. " +
                 "0 = pure throttle-driven sound. 1 = physics load can fully dominate.")]
        [Range(0f, 1f)]
        [SerializeField] private float mechanicalLoadInfluence = 0.4f;

        [Tooltip("Minimum audio load during engine braking (overrun). " +
                 "Non-zero so the synthesizer doesn't go silent on lift-off, " +
                 "preserving the overrun character that feeds the burble system.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float coastLoadFloor = 0.05f;

        [Tooltip("Audio load floor during rev limiter fuel cutoff (the 'cut' phase). " +
                 "Near-zero but not zero: valve train and mechanical inertia noise persists.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float revLimiterLoadScale = 0.08f;

        [Tooltip("Audio load peak during rev limiter fire phase (the 'fire' pulse). " +
                 "Each cycle: load spikes here then drops to revLimiterLoadScale.")]
        [Range(0f, 1f)]
        [SerializeField] private float revLimiterPeakLoad = 0.85f;

        [Tooltip("Rev limiter stutter frequency in Hz. " +
                 "1 Hz = 1 complete fire+cut cycle per second. " +
                 "Real street cars: 8–15 Hz. Race engines: 20–30 Hz.")]
        [Range(1f, 50f)]
        [SerializeField] private float revLimiterCutFrequency = 15f;

        // ── FMOD-equivalent Seek Speeds ────────────────────────────────────────
        [Header("FMOD-Equivalent Seek Speeds (Linear Rate Limiter)")]
        [Tooltip("Maximum RPM change per second allowed before VNS sees the value. " +
                 "FMOD Seek Speed is confirmed linear (units/second). " +
                 "This caps PhysX solver jitter (±30-50 RPM at 50 Hz) without " +
                 "the asymptotic lag of an exponential filter. " +
                 "Rule of thumb: full-rev sweep time = (maxRPM - idleRPM) / rpmSeekSpeed. " +
                 "Default 3000 RPM/s ≈ ~2.3 s sweep on a 0-7000 RPM range.")]
        [SerializeField] private float rpmSeekSpeed = 3000f;

        [Tooltip("Maximum load change per second when load is RISING (throttle-on). " +
                 "Mirrors FMOD's 'speed going up' asymmetric seek direction. " +
                 "Default 3.0 = full 0→1 load range in ~333 ms.")]
        [SerializeField] private float loadSeekSpeedUp = 3.0f;

        [Tooltip("Maximum load change per second when load is FALLING (throttle-off). " +
                 "Mirrors FMOD's 'speed going down' asymmetric seek direction. " +
                 "Slower than up to model exhaust energy decay + turbo spool-down. " +
                 "Default 1.5 = full 1→0 load range in ~667 ms.")]
        [SerializeField] private float loadSeekSpeedDown = 1.5f;

        // ── Private state ──────────────────────────────────────────────────────
        // Seek cursor state — where the parameter cursor currently sits,
        // exactly like an FMOD parameter value that is mid-seek.
        private float _seekRPM;
        private float _seekLoad;
        private int   _lastGear;

        // Rev limiter ping-pong phase (advances at 2 × cutFrequency Hz so
        // PingPong period of 2 maps to exactly 1 full fire+cut cycle per Hz).
        private float _revLimiterPhase = 0f;

        void OnEnable()
        {
            aG = GetComponent<VehicleNoiseSynthesizer>();
            vp = this.GetComponentInParent<VehicleController>();
            ep = vp.powertrain.engine;

            vp.powertrain.engine.onStart.AddListener(aG.TurnOn);
            vp.powertrain.engine.onStop.AddListener(aG.TurnOff);

            aG.Activate(vp.powertrain.engine.revLimiterRPM, vp.powertrain.engine.idleRPM);

            // FMOD equivalent: set parameter value BEFORE event start.
            // This bypasses seek speed for the initial frame — confirmed:
            // "You set a parameter value without seeking by setting the parameter
            //  value before calling Event start."
            // Source: https://qa.fmod.com/t/seek-issues-with-parameters/11352
            _seekRPM  = ep.idleRPM;
            _seekLoad = 0f;
            _lastGear = 0;
        }

        void OnDisable()
        {
            vp.powertrain.engine.onStart.RemoveListener(aG.TurnOn);
            vp.powertrain.engine.onStop.RemoveListener(aG.TurnOff);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // ─── RPM ────────────────────────────────────────────────────────────
            // LinearSeek = Mathf.MoveTowards: moves cursor toward target by at
            // most (rpmSeekSpeed × dt) RPM per frame.  Reaches target exactly —
            // no asymptotic lag, no hidden time-constant.
            // Caps PhysX solver jitter (±30-50 RPM at 50 Hz FixedUpdate) while
            // still tracking fast throttle blips accurately.
            float targetRPM = ep.OutputRPM;
            _seekRPM = LinearSeek(_seekRPM, targetRPM, rpmSeekSpeed, dt);
            aG.rpm   = _seekRPM;

            // ─── Audio Load ──────────────────────────────────────────────────────
            float driverThrottle = Mathf.Clamp01(vp.input.states.throttle);
            float mechLoad       = Mathf.Clamp01(ep.Load) * mechanicalLoadInfluence;
            float rawLoad        = Mathf.Max(driverThrottle, mechLoad);

            // ─── Rev Limiter Ping-Pong (seek bypass) ────────────────────────────
            // FMOD ignoreseekspeed = true equivalent.
            // The fire/cut stutter must be instantaneous — seek is bypassed
            // so each edge is razor-sharp, not ramped.
            if (ep.revLimiterActive)
            {
                _revLimiterPhase += dt * revLimiterCutFrequency * 2f;
                float pingPong = Mathf.PingPong(_revLimiterPhase, 1f);
                rawLoad    = Mathf.Lerp(revLimiterLoadScale, revLimiterPeakLoad, pingPong);
                _seekLoad  = rawLoad; // bypass seek
            }
            else
            {
                _revLimiterPhase = 0f;

                // ─── Coasting / Overrun ──────────────────────────────────────────
                if (driverThrottle < 0.02f && ep.RPMPercent > 0.15f)
                    rawLoad = Mathf.Max(rawLoad, coastLoadFloor);

                // ─── Asymmetric Linear Seek (Load) ──────────────────────────────
                // Rising load: loadSeekSpeedUp.  Falling load: loadSeekSpeedDown.
                // Both are strictly linear (Mathf.MoveTowards), matching FMOD's
                // confirmed asymmetric seek speed behaviour.
                float seekRate = rawLoad > _seekLoad ? loadSeekSpeedUp : loadSeekSpeedDown;
                _seekLoad = LinearSeek(_seekLoad, rawLoad, seekRate, dt);
            }

            aG.load = _seekLoad;

            // ─── Gear Change Detection ────────────────────────────────────────
            int currentGear = vp.powertrain.transmission.Gear;
            if (currentGear != _lastGear)
            {
                pitchOsc.OnGearChange();
                _lastGear = currentGear;
            }

            aG.shiftPitchOsc = pitchOsc.ProcessPitch(
                aG.rpm,
                aG.load,
                enableOscillation,
                Time.deltaTime
            );
        }

        /// <summary>
        /// Framerate-independent linear seek (bounded rate limiter).
        /// Wraps Mathf.MoveTowards: moves 'current' toward 'target' by at
        /// most (rate × dt) per frame.  Reaches target exactly — no lag.
        ///
        /// This is the correct C# equivalent of FMOD Seek Speed:
        /// "linear interpolation to smooth out parameter changes."
        /// Source: https://qa.fmod.com/t/change-parameter-over-time/11498
        /// </summary>
        private static float LinearSeek(float current, float target, float rate, float dt)
        {
            return Mathf.MoveTowards(current, target, rate * dt);
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
        public float revLimiterThreshold  = 50.0f;

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
        [SerializeField] private float lastRPM              = 0.0f;
        [SerializeField] private float oscillationIntensity = 0.0f;
        [SerializeField] private float delayTimer           = 0.0f;
        private float phaseOffset   = 0.0f;
        private float harmonicPhase = 0.0f;

        public float ProcessPitch(float currentRPM, float engineLoad, bool enableOscillation, float deltaTime)
        {
            if (!enableOscillation)
                return 0;

            float rpmDelta    = currentRPM - lastRPM;
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

            bool  isNearRevLimiter     = rpmDeltaAbs < revLimiterThreshold;
            float revLimiterMultiplier = isNearRevLimiter ? 1.5f : 1.0f;

            if (oscillationIntensity > 0)
                oscillationIntensity *= Mathf.Exp(-dampingFactor * deltaTime);

            float loadFactor = 1.0f + (engineLoad * loadInfluence);
            float baseSpeed  = oscillationSpeed * (1.0f + (currentRPM / 1000.0f));

            phaseOffset   = UpdatePhase(phaseOffset,   baseSpeed * deltaTime);
            harmonicPhase = UpdatePhase(harmonicPhase, baseSpeed * harmonicFrequency * deltaTime);

            float primaryOsc  = Mathf.Sin(phaseOffset);
            float harmonicOsc = Mathf.Sin(harmonicPhase) * harmonicAmplitude;
            float combinedOsc = (primaryOsc + harmonicOsc) / (1 + harmonicAmplitude);

            return combinedOsc *
                   oscillationDepth *
                   oscillationIntensity *
                   loadFactor *
                   revLimiterMultiplier;
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
            lastRPM       = 0.0f;
            phaseOffset   = 0.0f;
            harmonicPhase = 0.0f;
            delayTimer    = 0.0f;
        }
    }
}
