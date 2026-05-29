using UnityEngine;
#if !UNITY_WEBGL && UNITY_EDITOR
using UnityEditor;
#endif

namespace AroundTheGroundSimulator
{
    /// <summary>Vehicle Noise Synthesizer v1.9 — manages NativeArray disposal on domain reload and play-mode exit.</summary>
#if !UNITY_WEBGL
    internal static class VehicleNoiseBatchManagerLifetime
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                VehicleNoiseSynthesizer.VehicleNoiseBatchManager.DisposeAll();
            }
        }
#endif

        private static void OnQuitting()
        {
            Application.quitting -= OnQuitting;
            VehicleNoiseSynthesizer.VehicleNoiseBatchManager.DisposeAll();
        }
    }
#endif
}