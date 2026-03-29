#nullable enable
using System;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;

namespace Terrain.Editor.Input;

/// <summary>
/// Hybrid camera controller supporting orbit mode (default) and free-fly mode.
/// Orbit mode: right-drag rotates around center, middle-drag pans center, scroll zooms.
/// Free-fly mode: WASD movement, mouse look (activated while Shift held).
/// </summary>
public sealed class HybridCameraController
{
    // Configuration constants
    private const float DefaultRotationSpeed = 0.3f;
    private const float DefaultPanSpeed = 0.5f;
    private const float DefaultZoomSpeed = 5.0f;
    private const float DefaultFlySpeed = 50.0f;
    private const float MinOrbitDistance = 5.0f;
    private const float MaxOrbitDistance = 2000.0f;

    // Forward direction in Stride's coordinate system (negative Z)
    private static readonly Vector3 ForwardDirection = new Vector3(0, 0, -1);

    // Orbit state
    private Vector3 orbitCenter = Vector3.Zero;
    private float orbitDistance = 100.0f;
    private float yaw = 45.0f;   // degrees
    private float pitch = 30.0f; // degrees

    // Configuration properties
    public float RotationSpeed { get; set; } = DefaultRotationSpeed;
    public float PanSpeed { get; set; } = DefaultPanSpeed;
    public float ZoomSpeed { get; set; } = DefaultZoomSpeed;
    public float FlySpeed { get; set; } = DefaultFlySpeed;
    public float MinDistance { get; set; } = MinOrbitDistance;
    public float MaxDistance { get; set; } = MaxOrbitDistance;

    // State properties
    public CameraComponent? Camera { get; set; }
    public bool IsFlyModeActive { get; private set; }
    public Vector3 OrbitCenter
    {
        get => orbitCenter;
        set => orbitCenter = value;
    }
    public float OrbitDistance
    {
        get => orbitDistance;
        set => orbitDistance = MathUtil.Clamp(value, MinDistance, MaxDistance);
    }

    /// <summary>
    /// Updates camera based on input. Call this every frame.
    /// </summary>
    public void Update(float deltaTime, InputManager input)
    {
        if (Camera == null) return;

        // Detect fly mode toggle (Shift key held)
        IsFlyModeActive = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);

        if (IsFlyModeActive)
        {
            UpdateFlyMode(deltaTime, input);
        }
        else
        {
            UpdateOrbitMode(deltaTime, input);
        }
    }

    /// <summary>
    /// Resets orbit center and distance to view terrain bounds.
    /// </summary>
    public void ResetToTerrainBounds(float terrainWidth, float terrainHeight, float maxHeight)
    {
        orbitCenter = new Vector3(terrainWidth * 0.5f, maxHeight * 0.5f, terrainHeight * 0.5f);
        orbitDistance = Math.Max(terrainWidth, terrainHeight) * 1.5f;
        pitch = 30.0f;
        yaw = 45.0f;
        UpdateCameraTransform();
    }

    private void UpdateOrbitMode(float deltaTime, InputManager input)
    {
        // Right-drag: rotate around center
        if (input.IsMouseButtonDown(MouseButton.Right))
        {
            var delta = input.MouseDelta;
            yaw -= delta.X * RotationSpeed;
            pitch = MathUtil.Clamp(pitch - delta.Y * RotationSpeed, -89.0f, 89.0f);
        }

        // Middle-drag: pan orbit center in world space
        if (input.IsMouseButtonDown(MouseButton.Middle))
        {
            var delta = input.MouseDelta;

            // Calculate right and up vectors in world space
            var rotation = Quaternion.RotationYawPitchRoll(
                MathUtil.DegreesToRadians(yaw),
                MathUtil.DegreesToRadians(pitch),
                0);
            var rotationMatrix = Matrix.RotationQuaternion(rotation);
            var forward = rotationMatrix.Forward;
            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            var up = Vector3.UnitY;

            orbitCenter += right * -delta.X * PanSpeed;
            orbitCenter += up * delta.Y * PanSpeed;
        }

        // Scroll wheel: zoom (change orbit distance)
        var wheelDelta = input.MouseWheelDelta;
        if (wheelDelta != 0)
        {
            orbitDistance = MathUtil.Clamp(
                orbitDistance - wheelDelta * ZoomSpeed,
                MinDistance,
                MaxDistance);
        }

        UpdateCameraTransform();
    }

    private void UpdateFlyMode(float deltaTime, InputManager input)
    {
        var cameraEntity = Camera!.Entity;

        // Mouse look (while right button held in fly mode)
        if (input.IsMouseButtonDown(MouseButton.Right))
        {
            var delta = input.MouseDelta;
            yaw -= delta.X * RotationSpeed;
            pitch = MathUtil.Clamp(pitch - delta.Y * RotationSpeed, -89.0f, 89.0f);
        }

        // Calculate movement direction
        var rotation = Quaternion.RotationYawPitchRoll(
            MathUtil.DegreesToRadians(yaw),
            MathUtil.DegreesToRadians(pitch),
            0);
        var rotationMatrix = Matrix.RotationQuaternion(rotation);
        var forward = rotationMatrix.Forward;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.UnitY;

        var movement = Vector3.Zero;
        if (input.IsKeyDown(Keys.W)) movement += forward;
        if (input.IsKeyDown(Keys.S)) movement -= forward;
        if (input.IsKeyDown(Keys.A)) movement -= right;
        if (input.IsKeyDown(Keys.D)) movement += right;
        if (input.IsKeyDown(Keys.Q)) movement -= up;
        if (input.IsKeyDown(Keys.E)) movement += up;

        if (movement != Vector3.Zero)
        {
            movement = Vector3.Normalize(movement);
            cameraEntity.Transform.Position += movement * FlySpeed * deltaTime;
            orbitCenter = cameraEntity.Transform.Position + forward * orbitDistance;
        }

        // Apply rotation
        cameraEntity.Transform.Rotation = rotation;
    }

    private void UpdateCameraTransform()
    {
        if (Camera == null) return;

        var cameraEntity = Camera.Entity;
        var rotation = Quaternion.RotationYawPitchRoll(
            MathUtil.DegreesToRadians(yaw),
            MathUtil.DegreesToRadians(pitch),
            0);

        // Calculate camera position: orbit center + offset based on yaw/pitch
        // Use negative forward direction (backward) for orbit offset
        var rotationMatrix = Matrix.RotationQuaternion(rotation);
        var offset = -rotationMatrix.Forward * orbitDistance;
        cameraEntity.Transform.Position = orbitCenter + offset;
        cameraEntity.Transform.Rotation = rotation;
    }
}
