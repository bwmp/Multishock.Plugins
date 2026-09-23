using Valve.VR;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SteamVROverlay.Overlay;

/// <summary>
/// Hands rendered frames to SteamVR as D3D11 textures. <c>SetOverlayRaw</c> recreates the
/// overlay's texture on every call, which makes frequently updated panels flash; submitting a
/// GPU texture avoids that. Two textures alternate so a frame is never overwritten while the
/// compositor may still be reading it.
/// </summary>
public sealed class OverlayTexture : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D[] _textures;
    private readonly int _width;
    private readonly int _height;
    private int _next;

    public OverlayTexture(CVRSystem system, int width, int height)
    {
        _width = width;
        _height = height;

        // The texture must live on the GPU driving the headset.
        var adapterIndex = 0;
        system.GetDXGIOutputInfo(ref adapterIndex);

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1? adapter = null;
        if (adapterIndex < 0 || factory.EnumAdapters1((uint)adapterIndex, out adapter).Failure)
        {
            adapter = null;
        }

        try
        {
            D3D11.D3D11CreateDevice(
                adapter,
                adapter is null ? DriverType.Hardware : DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_0],
                out ID3D11Device? device,
                out ID3D11DeviceContext? context).CheckError();

            _device = device!;
            _context = context!;
        }
        finally
        {
            adapter?.Dispose();
        }

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
        };

        _textures = [_device.CreateTexture2D(description), _device.CreateTexture2D(description)];
    }

    /// <summary>
    /// Uploads RGBA pixels and assigns them to the overlay.
    /// </summary>
    public EVROverlayError Submit(CVROverlay overlay, ulong overlayHandle, IntPtr pixels)
    {
        var texture = _textures[_next];
        _next = (_next + 1) % _textures.Length;

        _context.UpdateSubresource(texture, 0, null, pixels, (uint)(_width * 4), (uint)(_width * _height * 4));
        _context.Flush();

        var vrTexture = new Texture_t
        {
            handle = texture.NativePointer,
            eType = ETextureType.DirectX,
            eColorSpace = EColorSpace.Auto,
        };

        return overlay.SetOverlayTexture(overlayHandle, ref vrTexture);
    }

    public void Dispose()
    {
        foreach (var texture in _textures)
        {
            texture.Dispose();
        }

        _context.Dispose();
        _device.Dispose();
    }
}
