using SmaetRec.Core;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;

namespace SmaetRec.Media;

/// <summary>The GPU device shared by capture, drawing and preview. Falls back to software (WARP) when no GPU is usable.</summary>
public sealed class GraphicsDevice : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public ID2D1Factory1 D2DFactory { get; }
    public ID2D1Device D2DDevice { get; }
    public ID2D1DeviceContext DC { get; }
    public IDWriteFactory DWrite { get; }
    public bool IsSoftware { get; }

    public GraphicsDevice(bool forceSoftware = false)
    {
        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        ID3D11Device? device = null; ID3D11DeviceContext? context = null;
        if (!forceSoftware)
        {
            var hr = D3D11.D3D11CreateDevice((IDXGIAdapter?)null!, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out device, out context);
            if (hr.Failure) { device = null; context = null; }
        }
        if (device == null)
        {
            D3D11.D3D11CreateDevice((IDXGIAdapter?)null!, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out device, out context).CheckError();
            IsSoftware = true;
        }
        Device = device!;
        Context = context!;
        using (var mt = Device.QueryInterfaceOrNull<ID3D11Multithread>()) mt?.SetMultithreadProtected(true);

        D2DFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.MultiThreaded);
        using (var dxgiDevice = Device.QueryInterface<IDXGIDevice>())
            D2DDevice = D2DFactory.CreateDevice(dxgiDevice);
        DC = D2DDevice.CreateDeviceContext(DeviceContextOptions.None);
        DC.UnitMode = UnitMode.Pixels;
        DWrite = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);
        Log.Write($"Graphics device ready ({(IsSoftware ? "software" : "hardware")}, feature level {Device.FeatureLevel})");
    }

    public static readonly Vortice.DCommon.PixelFormat Bgra = new(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);

    /// <summary>Creates a texture that Direct2D can draw into and that can be read back.</summary>
    public (ID3D11Texture2D texture, ID2D1Bitmap1 bitmap) CreateTarget(int width, int height)
    {
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource);
        var texture = Device.CreateTexture2D(desc);
        using var surface = texture.QueryInterface<IDXGISurface>();
        var bitmap = DC.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(Bgra, 96, 96, BitmapOptions.Target));
        return (texture, bitmap);
    }

    public ID3D11Texture2D CreateStaging(int width, int height)
    {
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read);
        return Device.CreateTexture2D(desc);
    }

    public void Dispose()
    {
        DC.Target = null;
        DWrite.Dispose();
        DC.Dispose();
        D2DDevice.Dispose();
        D2DFactory.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}
