using AroundTheGroundSimulator;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(VehicleNoiseSynthesizer))]
public class AudioGranulatorSimpleUI : MonoBehaviour
{
    private VehicleNoiseSynthesizer synthesizer;

    [Header("UI Controls")]
    public Slider rpmSlider;        // Reference to RPM UI slider
    public Slider loadSlider;       // Reference to Load UI slider
    public Text rpmText;            // Optional: Text to display current RPM
    public Text loadText;           // Optional: Text to display current Load

    [Header("RPM Settings")]
    public float minRPM = 800f;     // Minimum RPM value
    public float maxRPM = 8000f;    // Maximum RPM value

    [Header("Load Settings")]
    public float minLoad = 0f;      // Minimum Load value
    public float maxLoad = 1f;      // Maximum Load value

    private void Start()
    {
        // Get reference to the VehicleNoiseSynthesizer
        synthesizer = GetComponent<VehicleNoiseSynthesizer>();
        synthesizer._debug = true;

        // Initialize sliders
        InitializeSliders();

        // Add listeners to sliders
        rpmSlider.onValueChanged.AddListener(OnRPMChanged);
        loadSlider.onValueChanged.AddListener(OnLoadChanged);
    }

    private void InitializeSliders()
    {
        // Setup RPM slider
        if (rpmSlider != null)
        {
            rpmSlider.minValue = minRPM;
            rpmSlider.maxValue = maxRPM;
            rpmSlider.value = minRPM;
        }

        // Setup Load slider
        if (loadSlider != null)
        {
            loadSlider.minValue = minLoad;
            loadSlider.maxValue = maxLoad;
            loadSlider.value = minLoad;
        }

        synthesizer.Activate(maxRPM, minRPM);
        synthesizer.TurnOn();
        // Initial update of values
        UpdateSynthesizerValues();
    }

    private void OnRPMChanged(float value)
    {
        UpdateSynthesizerValues();
        UpdateUIText();
    }

    private void OnLoadChanged(float value)
    {
        UpdateSynthesizerValues();
        UpdateUIText();
    }

    private void UpdateSynthesizerValues()
    {
        if (synthesizer != null)
        {
            synthesizer.debug_rpm = rpmSlider.value;
            synthesizer.debug_load = loadSlider.value;
        }
    }

    private void UpdateUIText()
    {
        // Update RPM text if available
        if (rpmText != null)
        {
            rpmText.text = $"RPM: {rpmSlider.value:F0}";
        }

        // Update Load text if available
        if (loadText != null)
        {
            loadText.text = $"Load: {loadSlider.value:F2}";
        }
    }
}