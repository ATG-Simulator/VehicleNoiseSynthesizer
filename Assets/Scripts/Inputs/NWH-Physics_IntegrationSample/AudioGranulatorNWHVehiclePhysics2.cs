using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Powertrain;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace AroundTheGroundSimulator
{
    // NWH Vehicle integration sample
    [RequireComponent(typeof(VehicleNoiseSynthesizer))]
    public class AudioGranulatorNWHVehiclePhysics2 : MonoBehaviour
    {
        VehicleNoiseSynthesizer aG;

        VehicleController vp;
        EngineComponent ep;

        public bool enableOscillation = true;

        [SerializeField]
        EnginePitchOscillator pitchOsc = new EnginePitchOscillator();

        [Header("Audio Load Mapping")]
        [Tooltip("Blend factor for mechanical load into audio signal. " +
                 "0 = pure throttle-driven sound. 1 = physics load can fully dominate.")]
        [Range(0f, 1f)]
        [SerializeField] private float mechanicalLoadInfluence = 0.4f;

        [Tooltip("Smoothing rate when load is rising (throttle-on). " +
                 "Models intake manifold pressure rise time (~80ms).")]
        [SerializeField] private float loadAttackRate = 12f;

        [Tooltip("Smoothing rate when load is falling (throttle-off). " +
                 "Models exhaust energy decay + turbo spool-down (~150ms).")]
        [SerializeField] private float loadReleaseRate = 6f;

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

        [Tooltip("RPM smoothing rate. Filters physics solver jitter " +
                 "that would cause audible pitch stepping at 50Hz FixedUpdate.")]
        [SerializeField] private float rpmSmoothRate = 25f;

        // ── Private state ──
        private float _smoothedLoad;
        private float _smoothedRPM;
        private int _lastGear;

        // Rev limiter ping-pong phase (advances at 2 × cutFrequency Hz so
        // PingPong period of 2 maps to exactly 1 full fire+cut cycle per Hz).
        private float _revLimiterPhase = 0f;

        void OnEnable()
        {
            aG = GetComponent<VehicleNoiseSynthesizer>();
            vp = this.GetComponentInParent<VehicleController>();
            ep = vp.powertrain.engine;

            vp.powertrain.engine.onStart.AddListener(aG.TurnOn); //NWH Integration to know if the vehicle is on or off
            vp.powertrain.engine.onStop.AddListener(aG.TurnOff); //NWH Integration to know if the vehicle is on or off

            aG.Activate(vp.powertrain.engine.revLimiterRPM, vp.powertrain.engine.idleRPM);

            // Initialize smoothing state to avoid first-frame transients
            _smoothedLoad = 0f;
            _smoothedRPM = ep.idleRPM;
            _lastGear = 0;
        }

        void OnDisable()
        {
            vp.powertrain.engine.onStart.RemoveListener(aG.TurnOn); //NWH Integration to know if the vehicle is on or off
            vp.powertrain.engine.onStop.RemoveListener(aG.TurnOff); //NWH Integration to know if the vehicle is on or off
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // ─── RPM ────────────────────────────────────────────────────────
            // Exponential smoothing removes PhysX solver jitter (±30-50 RPM)
            // that would otherwise cause audible pitch stepping at 50 Hz.
            float targetRPM = ep.OutputRPM;
            _smoothedRPM = ExpSmooth(_smoothedRPM, targetRPM, rpmSmoothRate, dt);
            aG.rpm = _smoothedRPM;

            // ─── Audio Load ─────────────────────────────────────────────────
            // Two orthogonal signals drive exhaust note intensity:
            //
            //  1. Driver throttle (0–1): Primary acoustic indicator.
            //     Throttle plate angle → intake airflow volume + fuel injection
            //     quantity → dominant factor in exhaust pulse intensity.
            //     Source: processed input (dead-zone/curves applied by NWH).
            //
            //  2. Mechanical load (ep.Load, 0–1): Secondary modulator.
            //     Under drivetrain resistance, cylinder pressures rise,
            //     producing a "heavier" tone at the same throttle opening.
            //     Scaled by mechanicalLoadInfluence to control its weight.
            //
            // Combination via max() — industry standard (FMOD/Wwise vehicle
            // templates, Forza, Gran Turismo). Guarantees:
            //   Free-rev neutral:  throttle=1, mechLoad≈0  → 1.0  ✓
            //   WOT uphill:        throttle=1, mechLoad≈1  → 1.0  ✓
            //   Cruise half-throt: throttle=0.5, mechLoad=0.3 → 0.5  ✓
            //   Coast no throttle: throttle=0, mechLoad≈0  → ~0    ✓

            float driverThrottle = Mathf.Clamp01(vp.input.states.throttle);
            float mechLoad = Mathf.Clamp01(ep.Load) * mechanicalLoadInfluence;
            float rawLoad = Mathf.Max(driverThrottle, mechLoad);

            // ─── Rev Limiter Ping-Pong ──────────────────────────────────────
            // Real rev limiter = rapid fuel-cut stutter. Each cycle:
            //   FIRE phase: cylinders combust → rawLoad spikes to revLimiterPeakLoad
            //   CUT  phase: fuel cut → rawLoad drops to revLimiterLoadScale
            //
            // Implementation:
            //   _revLimiterPhase advances at (2 × freq) so Mathf.PingPong
            //   completes exactly 1 full 0→1→0 cycle per 1/freq seconds.
            //   Smoothing is bypassed so the stutter is sharp (no attack lag).
            if (ep.revLimiterActive)
            {
                // Advance triangular-wave phase (2 units per cycle = 1 Hz maps to 1 cycle)
                _revLimiterPhase += dt * revLimiterCutFrequency * 2f;

                // PingPong produces 0→1→0 triangular wave
                float pingPong = Mathf.PingPong(_revLimiterPhase, 1f);

                // Map 0→1 onto [floor, peak]: cut phase at 0, fire phase at 1
                rawLoad = Mathf.Lerp(revLimiterLoadScale, revLimiterPeakLoad, pingPong);

                // Bypass smoothing — stutter must be instantaneous, not filtered
                _smoothedLoad = rawLoad;
            }
            else
            {
                // Reset phase so next rev-limiter activation starts cleanly at cut
                _revLimiterPhase = 0f;

                // ─── Coasting / Overrun ─────────────────────────────────────────
                // Throttle released + RPM above idle = engine braking.
                // Real engines produce overrun pops/burbles from unburnt fuel
                // igniting in the exhaust manifold. Maintain a small floor so
                // VNS's burble system (which triggers on load delta) gets a
                // clean drop-to-floor rather than drop-to-zero.
                if (driverThrottle < 0.02f && ep.RPMPercent > 0.15f)
                    rawLoad = Mathf.Max(rawLoad, coastLoadFloor);

                // ─── Asymmetric Exponential Smoothing ───────────────────────────
                // Attack (throttle on) is faster than release (throttle off):
                //   Attack  ≈ intake manifold pressure rise (~80ms to peak)
                //   Release ≈ exhaust energy decay + turbo spool-down (~150ms)
                // This sits upstream of VNS's own loadTransitionTime (symmetric
                // lerp), giving us the correct physical envelope before VNS
                // applies its internal smoothing on top.
                float rate = rawLoad > _smoothedLoad ? loadAttackRate : loadReleaseRate;
                _smoothedLoad = ExpSmooth(_smoothedLoad, rawLoad, rate, dt);
            }

            aG.load = _smoothedLoad;

            // ─── Gear Change Detection ──────────────────────────────────────
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
        /// Framerate-independent exponential moving average.
        /// Equivalent to a first-order low-pass filter: cutoff ≈ rate / (2π) Hz.
        /// </summary>
        private static float ExpSmooth(float current, float target, float rate, float dt)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));
        }
    }
    [Serializable]
    public class EnginePitchOscillator
    {
        [Header("Core Oscillation")]
        public float oscillationSpeed = 8.0f;
        public float oscillationDepth = 0.15f;

        [Header("RPM Thresholds")]
        public float rpmIncreaseThreshold = 150.0f;  // Threshold for RPM increase
        public float rpmDecreaseThreshold = 80.0f;   // Threshold for RPM decrease
        public float revLimiterThreshold = 50.0f;    // Extra sensitivity near rev limiter

        [Header("Response Tuning")]
        public float dampingFactor = 5.0f;
        [Range(0.0f, 1.0f)]
        public float loadInfluence = 0.3f;
        public float effectDelay = 0.05f;            // Delay before effect kicks in

        [Header("Advanced Parameters")]
        public float harmonicFrequency = 2.0f;       // Secondary oscillation frequency
        public float harmonicAmplitude = 0.3f;       // Secondary oscillation strength
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

            // Calculate RPM change
            float rpmDelta = currentRPM - lastRPM;
            float rpmDeltaAbs = Mathf.Abs(rpmDelta);
            lastRPM = currentRPM;

            // Delay timer processing
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

            // Rev limiter zone detection
            bool isNearRevLimiter = rpmDeltaAbs < revLimiterThreshold;
            float revLimiterMultiplier = isNearRevLimiter ? 1.5f : 1.0f;

            // Update oscillation intensity
            if (oscillationIntensity > 0)
            {
                oscillationIntensity *= Mathf.Exp(-dampingFactor * deltaTime);
            }

            // Calculate load influence
            float loadFactor = 1.0f + (engineLoad * loadInfluence);

            // Update phases
            float baseSpeed = oscillationSpeed * (1.0f + (currentRPM / 1000.0f));
            phaseOffset = UpdatePhase(phaseOffset, baseSpeed * deltaTime);
            harmonicPhase = UpdatePhase(harmonicPhase, baseSpeed * harmonicFrequency * deltaTime);

            // Combine primary and harmonic oscillations
            float primaryOsc = Mathf.Sin(phaseOffset);
            float harmonicOsc = Mathf.Sin(harmonicPhase) * harmonicAmplitude;
            float combinedOsc = (primaryOsc + harmonicOsc) / (1 + harmonicAmplitude);

            // Calculate final oscillation
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