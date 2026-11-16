using System.Runtime.InteropServices;
using SmaetRec.Core;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using static SmaetRec.Media.Native;

namespace SmaetRec.Media;

/// <summary>A live picture. All methods are called on the render thread.</summary>
public interface IVideoSource : IDisposable
{
    /// <summary>Fetches the newest picture. Returns true when it changed.</summary>
    bool Update(GraphicsDevice gd);
    ID2D1Bitmap? Bitmap { get; }
    int Width { get; }
    int Height { get; }
    bool IsSimulated { get; }
    /// <summary>False once the thing being shown is gone (window closed, monitor unplugged).</summary>
    bool IsAlive { get; }
    /// <summary>Looks at a few spots of the current picture. True = all black, false = something visible, null = cannot tell.</summary>
    bool? ProbeBlack(GraphicsDevice gd) => null;
}

internal static class BlackProbe
{
    /// <summary>Sample points as fractions of the picture: centre, thirds and near the corners.</summary>
    public static readonly (double x, double y)[] Points =
    {
        (0.5, 0.5), (0.33, 0.33), (0.66, 0.33), (0.33, 0.66), (0.66, 0.66), (0.5, 0.2), (0.5, 0.8), (0.12, 0.5), (0.88, 0.5),
    };

    public static bool IsDark(byte b, byte g, byte r) => b < 12 && g < 12 && r < 12;
}

internal static class BitmapFormats
{
    public static readonly BitmapProperties1 Opaque =
        new(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), 96, 96, BitmapOptions.None);
}

/// <summary>Screen or window capture through Windows.Graphics.Capture (GPU, includes the mouse pointer).</summary>
public sealed class WgcScreenSource : IVideoSource
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    private static readonly Guid CaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Texture2DIid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private readonly CaptureTarget _target;
    private readonly GraphicsCaptureItem _item;
    private readonly IDirect3DDevice _winrtDevice;
    private Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private ID3D11Texture2D? _texture;
    private ID2D1Bitmap1? _bitmap;
    private volatile bool _closed;
    private Windows.Graphics.SizeInt32 _poolSize;

    public static bool IsSupported
    {
        get { try { return GraphicsCaptureSession.IsSupported(); } catch (Exception) { return false; } }
    }

    public WgcScreenSource(GraphicsDevice gd, CaptureTarget target)
    {
        _target = target;
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = CaptureItemIid;
        IntPtr itemPtr = target.Kind == CaptureKind.Monitor ? interop.CreateForMonitor(target.Handle, ref iid) : interop.CreateForWindow(target.Handle, ref iid);
        try { _item = GraphicsCaptureItem.FromAbi(itemPtr); } finally { Marshal.Release(itemPtr); }
        _item.Closed += (_, _) => _closed = true;

        using (var dxgi = gd.Device.QueryInterface<IDXGIDevice>())
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out IntPtr devicePtr));
            try { _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(devicePtr); } finally { Marshal.Release(devicePtr); }
        }
        RequestBorderlessOnce();
        _poolSize = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _session = _pool.CreateCaptureSession(_item);
        try
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
                _session.IsBorderRequired = false;
        }
        catch (Exception e) { Log.Write("Capture border could not be hidden: " + e.Message); }
        _session.IsCursorCaptureEnabled = true;
        _session.StartCapture();
    }

    private static bool _borderlessAsked;

    /// <summary>Windows only lets an app hide the yellow capture border after it has asked for that once.</summary>
    private static void RequestBorderlessOnce()
    {
        if (_borderlessAsked) return;
        _borderlessAsked = true;
        try
        {
            if (!Windows.Foundation.Metadata.ApiInformation.IsTypePresent("Windows.Graphics.Capture.GraphicsCaptureAccess")) return;
            var request = GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless).AsTask();
            if (!request.Wait(1500)) Log.Write("Borderless capture request did not answer in time");
            else Log.Write("Borderless capture: " + request.Result);
        }
        catch (Exception e) { Log.Write("Borderless capture not available: " + e.Message); }
    }

    public ID2D1Bitmap? Bitmap => _bitmap;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsSimulated => false;
    public bool IsAlive => !_closed && (_target.Kind == CaptureKind.Monitor || IsWindow(_target.Handle));

    public bool Update(GraphicsDevice gd)
    {
        Direct3D11CaptureFrame? latest = null;
        while (_pool.TryGetNextFrame() is { } frame) { latest?.Dispose(); latest = frame; }
        if (latest == null) return false;
        using (latest)
        {
            var size = latest.ContentSize;
            if (size.Width <= 0 || size.Height <= 0) return false;
            var access = latest.Surface.As<IDirect3DDxgiInterfaceAccess>();
            var iid = Texture2DIid;
            using var frameTexture = new ID3D11Texture2D(access.GetInterface(ref iid));
            var frameDesc = frameTexture.Description;
            int w = (int)Math.Min((uint)size.Width, frameDesc.Width), h = (int)Math.Min((uint)size.Height, frameDesc.Height);
            if (_texture == null || w != Width || h != Height)
            {
                _bitmap?.Dispose(); _texture?.Dispose();
                _texture = gd.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 1, BindFlags.ShaderResource));
                using var surface = _texture.QueryInterface<IDXGISurface>();
                _bitmap = gd.DC.CreateBitmapFromDxgiSurface(surface, BitmapFormats.Opaque);
                Width = w; Height = h;
            }
            gd.Context.CopySubresourceRegion(_texture, 0, 0, 0, 0, frameTexture, 0, new Vortice.Mathematics.Box(0, 0, 0, w, h, 1));
            if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
            {
                _poolSize = size;
                _pool.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            }
        }
        return true;
    }

    private ID3D11Texture2D? _probe;
    private const int ProbeSize = 4;

    public unsafe bool? ProbeBlack(GraphicsDevice gd)
    {
        if (_texture == null || Width < ProbeSize * 2 || Height < ProbeSize * 2) return null;
        int count = BlackProbe.Points.Length;
        _probe ??= gd.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)(ProbeSize * count), ProbeSize, 1, 1,
            BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        for (int i = 0; i < count; i++)
        {
            int x = Math.Clamp((int)(BlackProbe.Points[i].x * Width), 0, Width - ProbeSize), y = Math.Clamp((int)(BlackProbe.Points[i].y * Height), 0, Height - ProbeSize);
            gd.Context.CopySubresourceRegion(_probe, 0, (uint)(i * ProbeSize), 0, 0, _texture, 0, new Vortice.Mathematics.Box(x, y, 0, x + ProbeSize, y + ProbeSize, 1));
        }
        var map = gd.Context.Map(_probe, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            for (int row = 0; row < ProbeSize; row++)
            {
                byte* p = (byte*)map.DataPointer + row * (long)map.RowPitch;
                for (int n = 0; n < ProbeSize * count; n++, p += 4)
                    if (!BlackProbe.IsDark(p[0], p[1], p[2])) return false;
            }
            return true;
        }
        finally { gd.Context.Unmap(_probe, 0); }
    }

    public void Dispose()
    {
        try { _session.Dispose(); _pool.Dispose(); } catch (Exception e) { Log.Write("Capture close: " + e.Message); }
        _probe?.Dispose(); _probe = null;
        _bitmap?.Dispose(); _texture?.Dispose();
        _bitmap = null; _texture = null;
    }
}

/// <summary>Fallback capture with plain GDI, for machines where the modern capture API is unavailable.</summary>
public sealed class GdiScreenSource : IVideoSource
{
    private readonly CaptureTarget _target;
    private IntPtr _memDc, _dib, _oldObj, _bits;
    private ID2D1Bitmap1? _bitmap;
    private long _lastTick;
    private readonly int _intervalMs;

    public GdiScreenSource(CaptureTarget target, int fps = 20) { _target = target; _intervalMs = 1000 / Math.Max(1, fps); }

    public ID2D1Bitmap? Bitmap => _bitmap;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsSimulated => false;
    public bool IsAlive => _target.Kind == CaptureKind.Monitor || IsWindow(_target.Handle);

    public bool Update(GraphicsDevice gd)
    {
        long now = Environment.TickCount64;
        if (now - _lastTick < _intervalMs) return false;
        _lastTick = now;
        var (left, top, w, h) = _target.CurrentBounds();
        if (w <= 0 || h <= 0) return false;
        IntPtr screenDc = GetDC(IntPtr.Zero);
        try
        {
            if (_bitmap == null || w != Width || h != Height)
            {
                FreeGdi();
                _bitmap?.Dispose();
                var header = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                _memDc = CreateCompatibleDC(screenDc);
                _dib = CreateDIBSection(screenDc, ref header, 0, out _bits, IntPtr.Zero, 0);
                if (_dib == IntPtr.Zero) throw new InvalidOperationException("Could not create a capture bitmap");
                _oldObj = SelectObject(_memDc, _dib);
                _bitmap = gd.DC.CreateBitmap(new Vortice.Mathematics.SizeI(w, h), IntPtr.Zero, 0, BitmapFormats.Opaque);
                Width = w; Height = h;
            }
            if (_target.Kind == CaptureKind.Window)
            {
                if (!PrintWindow(_target.Handle, _memDc, 2)) BitBlt(_memDc, 0, 0, w, h, screenDc, left, top, SRCCOPY);
            }
            else
            {
                BitBlt(_memDc, 0, 0, w, h, screenDc, left, top, SRCCOPY | CAPTUREBLT);
                var cursor = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
                if (GetCursorInfo(ref cursor) && (cursor.flags & 1) != 0 && GetIconInfo(cursor.hCursor, out var icon))
                {
                    DrawIconEx(_memDc, cursor.ptScreenPos.X - left - icon.xHotspot, cursor.ptScreenPos.Y - top - icon.yHotspot, cursor.hCursor, 0, 0, 0, IntPtr.Zero, 3);
                    if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
                    if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
                }
            }
            _bitmap!.CopyFromMemory(_bits, (uint)(w * 4)).CheckError();
        }
        finally { ReleaseDC(IntPtr.Zero, screenDc); }
        return true;
    }

    public unsafe bool? ProbeBlack(GraphicsDevice gd)
    {
        if (_bits == IntPtr.Zero || Width < 2 || Height < 2) return null;
        foreach (var (fx, fy) in BlackProbe.Points)
        {
            int x = Math.Clamp((int)(fx * Width), 0, Width - 1), y = Math.Clamp((int)(fy * Height), 0, Height - 1);
            byte* p = (byte*)_bits + ((long)y * Width + x) * 4;
            if (!BlackProbe.IsDark(p[0], p[1], p[2])) return false;
        }
        return true;
    }

    private void FreeGdi()
    {
        if (_memDc != IntPtr.Zero) { SelectObject(_memDc, _oldObj); DeleteDC(_memDc); _memDc = IntPtr.Zero; }
        if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }
    }

    public void Dispose() { FreeGdi(); _bitmap?.Dispose(); _bitmap = null; }
}

/// <summary>
/// A picture that arrives as BGRA memory from another thread (the iPhone decoder, or the development test pattern).
/// </summary>
public sealed class MemoryFrameSource : IVideoSource
{
    private readonly object _lock = new();
    private byte[]? _pending;
    private int _pendingWidth, _pendingHeight;
    private bool _hasNew;
    private byte[]? _spare;
    private ID2D1Bitmap1? _bitmap;

    public MemoryFrameSource(bool simulated = false) => IsSimulated = simulated;

    public ID2D1Bitmap? Bitmap => _bitmap;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsSimulated { get; }
    public bool IsAlive => true;
    public event Action? FrameArrived;

    /// <summary>Borrow a buffer of at least the given size to fill with the next picture.</summary>
    public byte[] Rent(int bytes)
    {
        lock (_lock)
        {
            var b = _spare;
            _spare = null;
            return b != null && b.Length >= bytes ? b : new byte[bytes];
        }
    }

    /// <summary>Hands over a filled buffer (BGRA, stride = width * 4).</summary>
    public void Submit(byte[] bgra, int width, int height)
    {
        lock (_lock)
        {
            if (_pending != null && !ReferenceEquals(_pending, bgra)) _spare = _pending;
            _pending = bgra; _pendingWidth = width; _pendingHeight = height; _hasNew = true;
        }
        FrameArrived?.Invoke();
    }

    /// <summary>Copies the newest picture (BGRA). Returns false when there is none.</summary>
    public bool TryCopyLatest(out byte[] pixels, out int width, out int height)
    {
        lock (_lock)
        {
            width = _pendingWidth; height = _pendingHeight;
            if (_pending == null) { pixels = Array.Empty<byte>(); return false; }
            pixels = _pending.AsSpan(0, width * height * 4).ToArray();
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock) { _pending = null; _hasNew = true; }
        FrameArrived?.Invoke();
    }

    public bool Update(GraphicsDevice gd)
    {
        lock (_lock)
        {
            if (!_hasNew) return false;
            _hasNew = false;
            if (_pending == null)
            {
                _bitmap?.Dispose(); _bitmap = null; Width = Height = 0;
                return true;
            }
            if (_bitmap == null || Width != _pendingWidth || Height != _pendingHeight)
            {
                _bitmap?.Dispose();
                Width = _pendingWidth; Height = _pendingHeight;
                _bitmap = gd.DC.CreateBitmap(new Vortice.Mathematics.SizeI(Width, Height), IntPtr.Zero, 0, BitmapFormats.Opaque);
            }
            _bitmap.CopyFromMemory(_pending, (uint)(Width * 4)).CheckError();
            return true;
        }
    }

    public bool? ProbeBlack(GraphicsDevice gd)
    {
        lock (_lock)
        {
            if (_pending == null || _pendingWidth < 2 || _pendingHeight < 2) return null;
            foreach (var (fx, fy) in BlackProbe.Points)
            {
                int x = Math.Clamp((int)(fx * _pendingWidth), 0, _pendingWidth - 1), y = Math.Clamp((int)(fy * _pendingHeight), 0, _pendingHeight - 1);
                int i = (y * _pendingWidth + x) * 4;
                if (!BlackProbe.IsDark(_pending[i], _pending[i + 1], _pending[i + 2])) return false;
            }
            return true;
        }
    }

    /// <summary>Drops GPU objects after a device loss; the last picture is uploaded again on the next update.</summary>
    public void ResetDevice()
    {
        lock (_lock) { _bitmap?.Dispose(); _bitmap = null; Width = Height = 0; _hasNew = _pending != null; }
    }

    public void Dispose() { _bitmap?.Dispose(); _bitmap = null; }
}

/// <summary>Development-only moving test pattern. Always labelled as a simulation on screen.</summary>
public sealed class SimulatedFeed : IDisposable
{
    private readonly MemoryFrameSource _source;
    private readonly Timer _timer;
    private readonly int _width, _height;
    private int _frame;

    public SimulatedFeed(MemoryFrameSource source, int width, int height, int fps = 30)
    {
        _source = source; _width = width; _height = height;
        _timer = new Timer(_ => Tick(), null, 0, 1000 / fps);
    }

    private void Tick()
    {
        int n = Interlocked.Increment(ref _frame);
        var buffer = _source.Rent(_width * _height * 4);
        int box = Math.Max(8, _width / 6), bx = (n * 6) % Math.Max(1, _width - box), by = (int)((Math.Sin(n / 20.0) * 0.4 + 0.5) * (_height - box));
        for (int y = 0; y < _height; y++)
        {
            int row = y * _width * 4;
            for (int x = 0; x < _width; x++)
            {
                int i = row + x * 4;
                bool inBox = x >= bx && x < bx + box && y >= by && y < by + box;
                buffer[i] = inBox ? (byte)255 : (byte)(40 + 160 * x / _width);
                buffer[i + 1] = inBox ? (byte)200 : (byte)(60 + 120 * y / _height);
                buffer[i + 2] = inBox ? (byte)40 : (byte)(30 + ((x / 40 + y / 40) % 2) * 30);
                buffer[i + 3] = 255;
            }
        }
        _source.Submit(buffer, _width, _height);
    }

    public void Dispose() => _timer.Dispose();
}
