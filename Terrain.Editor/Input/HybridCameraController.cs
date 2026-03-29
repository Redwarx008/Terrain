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
    private const float DefaultFlySpeed = 50.0f;

    private Vector3 orbitCenter = Vector3.Zero;
    private float orbitDistance = 100.0f;
    private float yaw = 45.0f;
    private float pitch = 30.0f;
    private bool hasPendingCameraRefresh = true;

    public float RotationSpeed { get; set; } = DefaultRotationSpeed;
    public float FlySpeed { get; set; } = DefaultFlySpeed;

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
        orbitCenter = new Vector3(terrainWidth * 0.5f, maxHeight * 0.5f, terrainHeight * 0.5f);
        orbitDistance = Math.Max(terrainWidth, terrainHeight) * 1.5f;
        pitch = 30.0f;
        yaw = 45.0f;

        if (Camera == null)
        {
            return;
        }

        var cameraEntity = Camera.Entity;
        var rotation = Quaternion.RotationYawPitchRoll(
            MathUtil.DegreesToRadians(yaw),
            MathUtil.DegreesToRadians(pitch),
            0.0f);

        var rotationMatrix = Matrix.RotationQuaternion(rotation);
        var offset = -rotationMatrix.Forward * orbitDistance;
        cameraEntity.Transform.Position = orbitCenter + offset;
        cameraEntity.Transform.Rotation = rotation;
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
            float speed = FlySpeed * deltaTime * (speedBoost ? 4.0f : 1.0f);
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
}
