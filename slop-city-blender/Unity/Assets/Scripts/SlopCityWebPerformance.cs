#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Profiling;

namespace SlopCity
{
    // Browser-only telemetry measures Unity frames, not a separate JavaScript animation loop.
    public sealed class SlopCityWebPerformance : MonoBehaviour
    {
        [DllImport("__Internal")]
        private static extern void SlopCity_ReportFrameRate(float fps, float frameMs, float p95Ms, int width, int height);
        [DllImport("__Internal")]
        private static extern int SlopCity_IsMemoryDiagnosticsEnabled();
        [DllImport("__Internal")]
        private static extern void SlopCity_ReportMemory(double allocatedBytes, double reservedBytes, double managedUsedBytes,
            float x, float y, float z, float yaw, float pitch, int actualRenderingPath);
        private readonly float[] samples = new float[512];
        private double started, previous, warmupUntil;
        private int frames, count;
        private bool memoryDiagnostics;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var monitor = new GameObject("SlopCityBrowserPerformance");
            DontDestroyOnLoad(monitor);
            monitor.AddComponent<SlopCityWebPerformance>();
        }

        private void OnEnable()
        {
            memoryDiagnostics = SlopCity_IsMemoryDiagnosticsEnabled() != 0;
            ResetSample();
        }
        private void OnApplicationFocus(bool focused) { ResetSample(); }
        private void OnApplicationPause(bool paused) { ResetSample(); }
        private void ResetSample()
        {
            started = previous = Time.realtimeSinceStartupAsDouble;
            warmupUntil = started + 3d;
            frames = count = 0;
        }

        private void LateUpdate()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < warmupUntil) { started = previous = now; return; }
            if (count < samples.Length) samples[count++] = (float)((now - previous) * 1000d);
            previous = now;
            frames++;
            double duration = now - started;
            if (duration < 2d) return;
            Array.Sort(samples, 0, count);
            float p95 = count > 0 ? samples[Mathf.Clamp(Mathf.CeilToInt(count * .95f) - 1, 0, count - 1)] : 0f;
            SlopCity_ReportFrameRate((float)(frames / duration), (float)(duration * 1000d / frames), p95, Screen.width, Screen.height);
            // Explicit QA only; ordinary play does not query the memory profiler.
            if (memoryDiagnostics)
            {
                Camera camera = Camera.main;
                Vector3 position = camera ? camera.transform.position : Vector3.zero;
                Vector3 rotation = camera ? camera.transform.eulerAngles : Vector3.zero;
                SlopCity_ReportMemory(Profiler.GetTotalAllocatedMemoryLong(),
                    Profiler.GetTotalReservedMemoryLong(), Profiler.GetMonoUsedSizeLong(),
                    position.x, position.y, position.z, rotation.y, rotation.x,
                    camera ? (int)camera.actualRenderingPath : -1);
            }
            started = now;
            frames = count = 0;
        }
    }
}
#endif
