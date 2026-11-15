namespace SmaetRec.Core;

public static unsafe class PixelConvert
{
    /// <summary>Converts NV12 (BT.709 limited range, the usual phone/H.264 output) to BGRA.</summary>
    public static void Nv12ToBgra(byte* src, int srcStride, int width, int height, int uvOffsetRows, byte* dstBuffer, int dstStride)
    {
        nint yPlaneAddr = (nint)src, uvPlaneAddr = (nint)(src + (long)srcStride * uvOffsetRows), dstAddr = (nint)dstBuffer;
        Parallel.For(0, (height + 1) / 2, pair =>
        {
            byte* yPlane = (byte*)yPlaneAddr, uvPlane = (byte*)uvPlaneAddr, dst = (byte*)dstAddr;
            for (int k = 0; k < 2; k++)
            {
                int row = pair * 2 + k;
                if (row >= height) break;
                byte* y = yPlane + (long)row * srcStride;
                byte* uv = uvPlane + (long)pair * srcStride;
                byte* d = dst + (long)row * dstStride;
                for (int x = 0; x < width; x++)
                {
                    int c = (y[x] - 16) * 1192;
                    int ui = x & ~1;
                    int u = uv[ui] - 128, v = uv[ui + 1] - 128;
                    int r = (c + 1836 * v) >> 10;
                    int g = (c - 218 * u - 546 * v) >> 10;
                    int b = (c + 2163 * u) >> 10;
                    d[0] = (byte)(b < 0 ? 0 : b > 255 ? 255 : b);
                    d[1] = (byte)(g < 0 ? 0 : g > 255 ? 255 : g);
                    d[2] = (byte)(r < 0 ? 0 : r > 255 ? 255 : r);
                    d[3] = 255;
                    d += 4;
                }
            }
        });
    }
}
