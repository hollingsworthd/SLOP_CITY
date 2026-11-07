#if UNITY_WEBGL && !UNITY_EDITOR
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Exercises Burst IJob codegen on WebGL IL2CPP (playbook: fixed job buffers before expanding scope).
    /// </summary>
    internal static class SlopCityBurstWarmup
    {
        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct RootSumJob : IJob
        {
            public float4 terms;
            public float result;

            public void Execute()
            {
                result = math.sqrt(terms.x + terms.y + terms.z + terms.w);
            }
        }

        private static bool ran;

        public static void RunOnce()
        {
            if (ran) return;
            ran = true;
            try
            {
                var job = new RootSumJob { terms = new float4(1f, 4f, 9f, 16f) };
                job.Schedule().Complete();
                float sum = job.result;
                if (sum < 5.5f || sum > 6.5f)
                    Debug.LogWarning("Unexpected Burst warmup sum; diagnostics only.");
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("Burst warmup skipped: " + error.GetType().Name);
            }
        }
    }
}
#endif
