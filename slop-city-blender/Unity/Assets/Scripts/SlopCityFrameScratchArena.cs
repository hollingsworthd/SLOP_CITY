using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Reusable managed scratch for short-lived work (playbook: frame arena before heap churn).
    /// Main thread only; one consumer at a time per buffer kind.
    /// </summary>
    public static class SlopCityFrameScratchArena
    {
        private const int DefaultBytes = 65536;
        private static byte[] byteBuffer;
        private static float[] floatBuffer;

        public static byte[] Bytes(int minimumLength)
        {
            int need = minimumLength <= 0 ? DefaultBytes : minimumLength;
            if (byteBuffer == null || byteBuffer.Length < need)
                byteBuffer = new byte[Mathf.NextPowerOfTwo(need)];
            return byteBuffer;
        }

        public static float[] Floats(int minimumLength)
        {
            int need = minimumLength <= 0 ? 16 : minimumLength;
            if (floatBuffer == null || floatBuffer.Length < need)
                floatBuffer = new float[Mathf.NextPowerOfTwo(need)];
            return floatBuffer;
        }
    }
}
