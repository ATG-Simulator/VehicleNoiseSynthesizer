using AroundTheGroundSimulator;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(VehicleNoiseSynthesizer))]
public class AudioGranulatorSimpleUI : MonoBehaviour
{
    private VehicleNoiseSynthesizer synthesizer;

    [Header("UI Controls")]
    public Slider rpmSlider;
    public Slider loadSlider;
    public Text rpmText;
    public Text loadText;
    public Toggle autoLoadToggle;

    [Header("RPM Settings")]
    public float minRPM = 800f;
    public float maxRPM = 8000f;

    [Header("Load Settings")]
    public float minLoad = 0f;
    public float maxLoad = 1f;

    [Header("Response Settings")]
    [Range(0.1f, 10f)]
    public float smoothnessTime = 2f;
    [Range(0.1f, 10f)]
    public float loadResponseTime = 1f;
    [Range(100f, 2000f)]
    public float rpmChangeSpeed = 500f;
    [Range(0.1f, 2f)]
    public float loadChangeSpeed = 0.5f;

    [Header("Rev-Limiter Cutoff Effect")]
    [Tooltip("Enables a dramatic fuel-cut stutter when RPM hits the rev limiter (maxRPM).")]
    public bool enableCutoffEffect = true;
    [Range(0.9f, 1f)]
    [Tooltip("Normalised RPM (0-1) at which the effect triggers.  0.98 = 98% of maxRPM.")]
    public float cutoffThreshold = 0.98f;
    [Range(0.02f, 0.5f)]
    [Tooltip("Duration (seconds) of the fuel-cut silence window.")]
    public float cutoffDuration = 0.08f;
    [Range(0f, 0.3f)]
    [Tooltip("Random variation added to the cutoff duration for a more organic feel.")]
    public float cutoffDurationRandomness = 0.03f;
    [Range(0.05f, 1f)]
    [Tooltip("Cooldown after a cutoff ends before another can fire.  Prevents rapid re-triggering.")]
    public float cutoffCooldown = 0.25f;

    private float currentRPM;
    private float targetRPM;
    private float currentLoad;
    private float targetLoad;
    private float lastRPMValue;
    private float rpmChangeRate;
    private bool isAutoLoad = true;
    private float lastUpdateTime;

    private float cutoffTimer;
    private bool cutoffActive;
    private float cutoffCooldownTimer;
    private float savedLoadBeforeCutoff;

    private void Start()
    {
        synthesizer = GetComponent<VehicleNoiseSynthesizer>();
        synthesizer.debug = true;

        InitializeValues();
        SetupListeners();

        synthesizer.Activate();
        synthesizer.TurnOn();
    }

    private void InitializeValues()
    {
        currentRPM = minRPM;
        targetRPM = minRPM;
        currentLoad = minLoad;
        targetLoad = minLoad;
        lastRPMValue = minRPM;

        if (rpmSlider != null)
        {
            rpmSlider.minValue = minRPM;
            rpmSlider.maxValue = maxRPM;
            rpmSlider.value = minRPM;
        }

        if (loadSlider != null)
        {
            loadSlider.minValue = minLoad;
            loadSlider.maxValue = maxLoad;
            loadSlider.value = minLoad;
        }

        if (autoLoadToggle != null)
        {
            autoLoadToggle.isOn = isAutoLoad;
        }
    }

    private void SetupListeners()
    {
        if (rpmSlider != null)
            rpmSlider.onValueChanged.AddListener(OnRPMChanged);

        if (loadSlider != null)
            loadSlider.onValueChanged.AddListener(OnLoadChanged);

        if (autoLoadToggle != null)
            autoLoadToggle.onValueChanged.AddListener(OnAutoLoadToggled);
    }

    private void Update()
    {
        HandleKeyboardInput();
        UpdateValues();
        UpdateCutoffEffect();
        ApplyValues();
        UpdateUI();
    }

    /// <summary>Dramatic rev-limiter fuel-cut effect. When RPM approaches maxRPM, briefly cuts engine load to zero, creating the characteristic stutter of a real rev limiter.</summary>
    private void UpdateCutoffEffect()
    {
        if (!enableCutoffEffect) return;

        float normalizedRPM = Mathf.InverseLerp(minRPM, maxRPM, currentRPM);

        if (cutoffCooldownTimer > 0f)
            cutoffCooldownTimer -= Time.deltaTime;

        if (!cutoffActive && cutoffCooldownTimer <= 0f && normalizedRPM >= cutoffThreshold)
        {
            cutoffActive = true;
            savedLoadBeforeCutoff = currentLoad;
            cutoffTimer = cutoffDuration + Random.Range(-cutoffDurationRandomness, cutoffDurationRandomness);
        }

        if (cutoffActive)
        {
            currentLoad = 0f;
            cutoffTimer -= Time.deltaTime;
            if (cutoffTimer <= 0f)
            {
                cutoffActive = false;
                cutoffCooldownTimer = cutoffCooldown;
                currentLoad = savedLoadBeforeCutoff;
            }
        }
    }

    private void HandleKeyboardInput()
    {
        float deltaTime = Time.deltaTime;

        if (IsKeyHeld(KeyCode.UpArrow))
        {
            targetRPM = Mathf.Min(targetRPM + rpmChangeSpeed * deltaTime, maxRPM);
            if (isAutoLoad)
            {
                targetLoad = Mathf.Min(targetLoad + loadChangeSpeed * deltaTime, maxLoad);
            }
        }
        else if (IsKeyHeld(KeyCode.DownArrow))
        {
            targetRPM = Mathf.Max(targetRPM - rpmChangeSpeed * deltaTime, minRPM);
            if (isAutoLoad)
            {
                targetLoad = Mathf.Max(targetLoad - loadChangeSpeed * deltaTime, minLoad);
            }
        }

        if (!isAutoLoad)
        {
            if (IsKeyHeld(KeyCode.RightArrow))
            {
                targetLoad = Mathf.Min(targetLoad + loadChangeSpeed * deltaTime, maxLoad);
            }
            else if (IsKeyHeld(KeyCode.LeftArrow))
            {
                targetLoad = Mathf.Max(targetLoad - loadChangeSpeed * deltaTime, minLoad);
            }
        }

        if (rpmSlider != null) rpmSlider.value = targetRPM;
        if (loadSlider != null) loadSlider.value = targetLoad;
    }

    private static bool IsKeyHeld(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            return key switch
            {
                KeyCode.UpArrow => kb.upArrowKey.isPressed,
                KeyCode.DownArrow => kb.downArrowKey.isPressed,
                KeyCode.LeftArrow => kb.leftArrowKey.isPressed,
                KeyCode.RightArrow => kb.rightArrowKey.isPressed,
                _ => false
            };
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(key);
#else
        return false;
#endif
    }

    private void UpdateValues()
    {
        float deltaTime = Time.deltaTime;

        rpmChangeRate = (targetRPM - lastRPMValue) / deltaTime;
        lastRPMValue = targetRPM;

        currentRPM = Mathf.Lerp(currentRPM, targetRPM, deltaTime * smoothnessTime);

        if (isAutoLoad)
        {
            UpdateAutoLoad();
        }
        else
        {
            currentLoad = Mathf.Lerp(currentLoad, targetLoad, deltaTime * smoothnessTime);
        }
    }

    private void UpdateAutoLoad()
    {
        float normalizedRPM = Mathf.InverseLerp(minRPM, maxRPM, currentRPM);
        float targetLoad;
        float rpmChangeThreshold = 50f;

        if (rpmChangeRate > rpmChangeThreshold)
        {
            targetLoad = Mathf.Lerp(currentLoad, 1f, Time.deltaTime * loadResponseTime);
        }
        else if (rpmChangeRate < -rpmChangeThreshold)
        {
            targetLoad = Mathf.Lerp(currentLoad, 0f, Time.deltaTime * loadResponseTime);
        }
        else
        {
            targetLoad = Mathf.Lerp(currentLoad, 0.85f, Time.deltaTime * loadResponseTime);
        }

        currentLoad = Mathf.Clamp(targetLoad, minLoad, maxLoad);
        loadSlider.value = currentLoad;
    }

    private void OnAutoLoadToggled(bool value)
    {
        isAutoLoad = value;
        if (loadSlider != null)
            loadSlider.interactable = !value;
    }

    private void OnRPMChanged(float value)
    {
        targetRPM = value;
    }

    private void OnLoadChanged(float value)
    {
        if (!isAutoLoad)
        {
            targetLoad = value;
        }
    }

    private void ApplyValues()
    {
        if (synthesizer != null)
        {
            synthesizer.debugrpm = currentRPM;
            synthesizer.debugload = currentLoad;
        }
    }

    private void UpdateUI()
    {
        if (rpmText != null)
        {
            rpmText.text = $"RPM: {currentRPM:F0}";
        }

        if (loadText != null)
        {
            loadText.text = $"Load: {currentLoad:F2}";
        }
    }
}
