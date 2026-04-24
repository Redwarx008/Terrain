#nullable enable

using System;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.SharedTexture;

public sealed class HeadlessStrideGraphicsDeviceHost : IDisposable
{
    private static readonly GraphicsProfile[] RequiredProfiles =
    [
        GraphicsProfile.Level_11_1,
    ];

    private readonly GraphicsDevice? _graphicsDevice;

    public HeadlessStrideGraphicsDeviceHost()
    {
        try
        {
            _graphicsDevice = GraphicsDevice.New(DeviceCreationFlags.BgraSupport, RequiredProfiles);

            if (GraphicsDevice.Platform != GraphicsPlatform.Direct3D11)
            {
                string platform = GraphicsDevice.Platform.ToString();
                _graphicsDevice.Dispose();
                _graphicsDevice = null;
                FailureMessage = $"Stride shared texture viewport requires Direct3D11; current graphics platform is {platform}.";
                return;
            }

            GraphicsProfile profile = _graphicsDevice.Features.CurrentProfile;
            if (profile < GraphicsProfile.Level_11_1)
            {
                _graphicsDevice.Dispose();
                _graphicsDevice = null;
                FailureMessage = $"Stride GraphicsDevice profile {profile} does not support shared NT texture handles.";
                return;
            }

            Status = $"Stride headless GraphicsDevice ready: {_graphicsDevice.RendererName}, profile {profile}.";
        }
        catch (Exception exception)
        {
            FailureMessage = $"Failed to create headless Stride GraphicsDevice: {exception.Message}";
        }
    }

    public GraphicsDevice? GraphicsDevice => _graphicsDevice;

    public bool IsAvailable => _graphicsDevice != null;

    public string Status { get; } = "Stride headless GraphicsDevice is unavailable.";

    public string? FailureMessage { get; }

    public void Dispose()
    {
        _graphicsDevice?.Dispose();
    }
}
