#nullable enable
using System;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using NumericsVector2 = System.Numerics.Vector2;

namespace Terrain.Editor.Input;

/// <summary>
/// Fly-only camera controller for the editor viewport.
/// Input collection stays outside this class; this class only applies fly camera transforms.
/// </summary>
public sealed class HybridCameraController
{
    private const float DefaultRotationSpeed = 0.3f;

    /// <summary>
    /// Speed presets in units per second. Index 2 (50) matches the original default.
    /// </summary>
    private static readonly float[] SpeedPresets = [10, 25, 50, 100, 250, 500, 1000, 2500, 5000];
    private const int DefaultSpeedIndex = 2; // 50 units/sec

    private Vector3 orbitCenter = Vector3.Zero;
    private float orbitDistance = 100.0f;
    private float yaw = 45.0f;
    private float pitch = 30.0f;
    private bool hasPendingCameraRefresh = true;

    public float RotationSpeed { get; set; } = DefaultRotationSpeed;

    /// <summary>
    /// Current fly speed in units per second. Automatically synced with SpeedPresetIndex.
    /// </summary>
    public float FlySpeed { get; private set; } = SpeedPresets[DefaultSpeedIndex];

    /// <summary>
    /// Current index into SpeedPresets array.
    /// </summary>
    public int SpeedPresetIndex { get; private set; } = DefaultSpeedIndex;

    /// <summary>
    /// Current speed value from the presets array.
    /// </summary>
    public float CurrentSpeed => SpeedPresets[SpeedPresetIndex];

    /// <summary>
    /// Number of available speed presets.
    /// </summary>
    public static int SpeedPresetCount => SpeedPresets.Length;

    /// <summary>
    /// Event raised when speed preset changes.
    /// </summary>
    public event Action<int>? SpeedPresetChanged;

    /// <summary>
    /// Adjusts speed by the given number of preset steps. Positive increases, negative decreases.
    /// </summary>
    public void AdjustSpeed(int delta)
    {
        int newIndex = Math.Clamp(SpeedPresetIndex + delta, 0, SpeedPresets.Length - 1);
        if (newIndex != SpeedPresetIndex)
        {
            SpeedPresetIndex = newIndex;
            FlySpeed = SpeedPresets[SpeedPresetIndex];
            SpeedPresetChanged?.Invoke(SpeedPresetIndex);
        }
    }

    /// <summary>
    /// Sets speed to a specific preset index.
    /// </summary>
    public void SetSpeedPreset(int index)
    {
        int clampedIndex = Math.Clamp(index, 0, SpeedPresets.Length - 1);
        if (clampedIndex != SpeedPresetIndex)
        {
            SpeedPresetIndex = clampedIndex;
            FlySpeed = SpeedPresets[SpeedPresetIndex];
            SpeedPresetChanged?.Invoke(SpeedPresetIndex);
        }
    }

    public CameraComponent? Camera { get; set; }
    public bool IsFlyModeActive { get; private set; } = true;
    public Vector3 OrbitCenter
    {
        get => orbitCenter;
        set => orbitCenter = value;
    }

    public float OrbitDistance => orbitDistance;

    public float YawDegrees => yaw;
    public float PitchDegrees => pitch;
    public Vector3 CameraPosition => Camera?.Entity?.Transform.Position ?? Vector3.Zero;
    public bool HasPendingCameraRefresh => hasPendingCameraRefresh;

    public void Update(float deltaTime, InputManager input)
    {
        if (Camera == null)
        {
            return;
        }

        UpdateFlyMode(
            deltaTime,
            input.MouseDelta,
            input.IsMouseButtonDown(MouseButton.Right),
            input.IsKeyDown(Keys.W),
            input.IsKeyDown(Keys.S),
            input.IsKeyDown(Keys.A),
            input.IsKeyDown(Keys.D),
            input.IsKeyDown(Keys.Q),
            input.IsKeyDown(Keys.E),
            input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift));
    }

    public void UpdateFromViewportInput(
        float deltaTime,
        NumericsVector2 mouseDelta,
        float mouseWheelDelta,
        bool rightMouseDown,
        bool middleMouseDown,
        bool moveForward,
        bool moveBackward,
        bool moveLeft,
        bool moveRight,
        bool moveDown,
        bool moveUp,
        bool flyModifier)
    {
        if (Camera == null)
        {
            return;
        }

        // Handle speed adjustment via mouse wheel when right mouse button is held
        if (rightMouseDown && mouseWheelDelta != 0)
        {
            int delta = Math.Sign(mouseWheelDelta);
            // Shift modifier doubles the adjustment speed
            if (flyModifier) delta *= 2;
            AdjustSpeed(delta);
        }

        UpdateFlyMode(
            deltaTime,
            mouseDelta,
            rightMouseDown,
            moveForward,
            moveBackward,
            moveLeft,
            moveRight,
            moveDown,
            moveUp,
            flyModifier);
    }

    /// <summary>
    /// Keep this hook for viewport resize/init flows without changing the controller math itself.
    /// </summary>
    public void RefreshCameraMatrices(float? aspectRatio = null)
    {
        if (Camera == null)
        {
            return;
        }

        Camera.Entity.Transform.UpdateWorldMatrix();
        Camera.Update(aspectRatio);
        hasPendingCameraRefresh = false;
    }

    public void MarkCameraRefreshPending()
    {
        hasPendingCameraRefresh = true;
    }

    public void ResetToTerrainBounds(float terrainWidth, float terrainHeight, float maxHeight)
    {
        float terrainExtent = Math.Max(terrainWidth, terrainHeight);
        orbitCenter = new Vector3(terrainWidth * 0.5f, maxHeight * 0.5f, terrainHeight * 0.5f);
        orbitDistance = terrainExtent * 0.9f;

        if (Camera == null)
        {
            return;
        }

        var cameraEntity = Camera.Entity;
        float horizontalOffset = terrainExtent * 0.32f;
        float verticalOffset = Math.Max(maxHeight + terrainExtent * 0.10f, 40.0f);

        // Place the editor camera from an explicit point above the terrain instead of reconstructing
        // the position from yaw/pitch. That old path could land below the terrain because the local
        // forward convention here is easy to get wrong and flips the offset sign. Keep the horizontal
        // offset tied to the terrain extent itself so large terrains do not push the camera into
        // negative X/Z before the user even starts navigating.
        var cameraPosition = new Vector3(
            orbitCenter.X - horizontalOffset,
            verticalOffset,
            orbitCenter.Z - horizontalOffset);
        var forward = Vector3.Normalize(orbitCenter - cameraPosition);
        var rotation = Quaternion.LookRotation(forward, Vector3.UnitY);

        cameraEntity.Transform.Position = cameraPosition;
        cameraEntity.Transform.Rotation = rotation;
        SyncAnglesFromForward(forward);
        hasPendingCameraRefresh = true;
    }

    private void UpdateFlyMode(
        float deltaTime,
        NumericsVector2 mouseDelta,
        bool rightMouseDown,
        bool moveForward,
        bool moveBackward,
        bool moveLeft,
        bool moveRight,
        bool moveDown,
        bool moveUp,
        bool speedBoost)
    {
        var cameraEntity = Camera!.Entity;
        bool transformChanged = false;

        if (rightMouseDown)
        {
            yaw -= mouseDelta.X * RotationSpeed;
            pitch = MathUtil.Clamp(pitch - mouseDelta.Y * RotationSpeed, -89.0f, 89.0f);
            transformChanged = true;
        }

        var rotation = Quaternion.RotationYawPitchRoll(
            MathUtil.DegreesToRadians(yaw),
            MathUtil.DegreesToRadians(pitch),
            0.0f);
        var rotationMatrix = Matrix.RotationQuaternion(rotation);
        var forward = rotationMatrix.Forward;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

        var movement = Vector3.Zero;
        if (moveForward) movement += forward;
        if (moveBackward) movement -= forward;
        if (moveLeft) movement -= right;
        if (moveRight) movement += right;
        if (moveDown) movement -= Vector3.UnitY;
        if (moveUp) movement += Vector3.UnitY;

        if (movement != Vector3.Zero)
        {
            movement = Vector3.Normalize(movement);
            float speed = FlySpeed * deltaTime * (speedBoost ? 2.0f : 1.0f);
            cameraEntity.Transform.Position += movement * speed;
            orbitCenter = cameraEntity.Transform.Position + forward * orbitDistance;
            transformChanged = true;
        }

        cameraEntity.Transform.Rotation = rotation;
        if (transformChanged)
        {
            hasPendingCameraRefresh = true;
        }
    }

    private void SyncAnglesFromForward(Vector3 forward)
    {
        yaw = MathUtil.RadiansToDegrees(MathF.Atan2(-forward.X, forward.Z));
        pitch = MathUtil.RadiansToDegrees(MathF.Asin(MathUtil.Clamp(forward.Y, -1.0f, 1.0f)));
    }
}
