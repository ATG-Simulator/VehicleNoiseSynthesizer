using UnityEngine;
#if !UNITY_WEBGL && UNITY_EDITOR
using UnityEditor;
#endif

namespace AroundTheGroundSimulator
{
    /// <summary>
    /// Disposes VNS batch NativeArrays when leaving play mode or quitting
    /// (non-WebGL). Prevents Persistent allocator leaks across domain reloads.
    /// </summary>
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