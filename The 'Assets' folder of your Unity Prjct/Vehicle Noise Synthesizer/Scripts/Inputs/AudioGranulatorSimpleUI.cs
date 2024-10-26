using AroundTheGroundSimulator;
using UnityEngine;
using UnityEngine.UI;

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

    // Private variables for smoothing
    private float currentRPM;
    private float targetRPM;
    private float currentLoad;
    private float targetLoad;
    private float lastRPMValue;
    private float rpmChangeRate;
    private bool isAutoLoad = false;
    private float lastUpdateTime;

    private void Start()
    {
        synthesizer = GetComponent<VehicleNoiseSynthesizer>();
        synthesizer._debug = true;

        InitializeValues();
        SetupListeners();

        synthesizer.Activate(maxRPM, minRPM);
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
        ApplyValues();
        UpdateUI();
    }

    private void HandleKeyboardInput()
    {
        float deltaTime = Time.deltaTime;

        // Up/Down arrows control RPM
        if (Input.GetKey(KeyCode.UpArrow))
        {
            targetRPM = Mathf.Min(targetRPM + rpmChangeSpeed * deltaTime, maxRPM);
            if (isAutoLoad)
            {
                targetLoad = Mathf.Min(targetLoad + loadChangeSpeed * deltaTime, maxLoad);
            }
        }
        else if (Input.GetKey(KeyCode.DownArrow))
        {
            targetRPM = Mathf.Max(targetRPM - rpmChangeSpeed * deltaTime, minRPM);
            if (isAutoLoad)
            {
                targetLoad = Mathf.Max(targetLoad - loadChangeSpeed * deltaTime, minLoad);
            }
        }

        // Left/Right arrows control Load when not in auto mode
        if (!isAutoLoad)
        {
            if (Input.GetKey(KeyCode.RightArrow))
            {
                targetLoad = Mathf.Min(targetLoad + loadChangeSpeed * deltaTime, maxLoad);
            }
            else if (Input.GetKey(KeyCode.LeftArrow))
            {
                targetLoad = Mathf.Max(targetLoad - loadChangeSpeed * deltaTime, minLoad);
            }
        }

        // Update slider values to match targets
        if (rpmSlider != null) rpmSlider.value = targetRPM;
        if (loadSlider != null) loadSlider.value = targetLoad;
    }

    private void UpdateValues()
    {
        float deltaTime = Time.deltaTime;

        // Calculate RPM change rate
        rpmChangeRate = (targetRPM - lastRPMValue) / deltaTime;
        lastRPMValue = targetRPM;

        // Smooth RPM changes
        currentRPM = Mathf.Lerp(currentRPM, targetRPM, deltaTime * smoothnessTime);

        if (isAutoLoad)
        {
            UpdateAutoLoad();
        }
        else
        {
            // Smooth Load changes
            currentLoad = Mathf.Lerp(currentLoad, targetLoad, deltaTime * smoothnessTime);
        }
    }

    private void UpdateAutoLoad()
    {
        float normalizedRPM = Mathf.InverseLerp(minRPM, maxRPM, currentRPM);
        float targetLoad;

        // Calculate RPM change
        float rpmChangeThreshold = 50f; // Threshold to determine if RPM is "stable"

        if (rpmChangeRate > rpmChangeThreshold) // RPM increasing
        {
            targetLoad = Mathf.Lerp(currentLoad, 1f, Time.deltaTime * loadResponseTime);
        }
        else if (rpmChangeRate < -rpmChangeThreshold) // RPM decreasing significantly
        {
            targetLoad = Mathf.Lerp(currentLoad, 0f, Time.deltaTime * loadResponseTime);
        }
        else // RPM stable - maintain load to simulate resistance
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
            synthesizer.debug_rpm = currentRPM;
            synthesizer.debug_load = currentLoad;
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