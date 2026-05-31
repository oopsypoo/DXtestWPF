using System.Numerics;

namespace DXtestWPF;

/// <summary>
/// First-person orbital camera for the Direct3D 11 demo.
///
/// Coordinate system: right-handed, Y-up (matches Direct3D convention when
/// the projection matrix is built with <see cref="Matrix4x4.CreatePerspectiveFieldOfView"/>).
///
/// Responsibilities
/// ----------------
///   • Maintain position, yaw (horizontal) and pitch (vertical) angles.
///   • Expose a ready-to-use <see cref="ViewMatrix"/>.
///   • Accept a <see cref="CameraInput"/> snapshot each frame and advance
///     the camera state by <paramref name="deltaTime"/> seconds.
///
/// Threading
/// ---------
/// All state is read/written on the render thread via <see cref="Update"/>.
/// <see cref="ViewMatrix"/> may be read on the render thread after Update.
/// <see cref="ApplyInput"/> may be called from any thread; it uses a lock
/// to swap the pending input atomically.
/// </summary>
public sealed class Camera
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    /// <summary>World-space up vector.</summary>
    private static readonly Vector3 WorldUp = Vector3.UnitY;

    // -----------------------------------------------------------------------
    // Configuration (public, safe to set before the render thread starts)
    // -----------------------------------------------------------------------

    /// <summary>Movement speed in world units per second.</summary>
    public float MoveSpeed { get; set; } = 4.0f;

    /// <summary>How far the camera can go from the world origin on any axis.</summary>
    public float MaxDistance { get; set; } = 50.0f;

    /// <summary>Maximum upward pitch angle in radians (prevents gimbal flip).</summary>
    public float MaxPitch { get; set; } = MathF.PI / 2.0f - 0.05f;

    // -----------------------------------------------------------------------
    // State (render-thread only after initialisation)
    // -----------------------------------------------------------------------

    private Vector3 _position;
    private float _yaw;    // rotation around world-Y, radians
    private float _pitch;  // rotation around local-X, radians

    // -----------------------------------------------------------------------
    // Input exchange (cross-thread)
    // -----------------------------------------------------------------------

    private CameraInput _pendingInput;
    private readonly object _inputLock = new();

    // -----------------------------------------------------------------------
    // Cached output (render-thread only)
    // -----------------------------------------------------------------------

    private Matrix4x4 _viewMatrix = Matrix4x4.Identity;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a camera at the given initial position looking toward the origin.
    /// </summary>
    public Camera(Vector3 initialPosition)
    {
        _position = initialPosition;

        // Derive initial yaw/pitch so the forward vector points at the origin.
        Vector3 toOrigin = Vector3.Normalize(Vector3.Zero - initialPosition);
        _yaw = MathF.Atan2(toOrigin.X, toOrigin.Z);
        _pitch = MathF.Asin(Math.Clamp(toOrigin.Y, -1.0f, 1.0f));

        RebuildViewMatrix();
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// The current view matrix. Valid after every call to <see cref="Update"/>.
    /// Read this on the render thread to build the WorldViewProjection constant.
    /// </summary>
    public Matrix4x4 ViewMatrix => _viewMatrix;

    /// <summary>
    /// Current camera position in world space (read-only snapshot).
    /// </summary>
    public Vector3 Position => _position;

    /// <summary>
    /// Submit a new input snapshot from the UI thread (or any thread).
    /// The render thread picks it up on the next <see cref="Update"/> call.
    /// </summary>
    public void ApplyInput(CameraInput input)
    {
        lock (_inputLock)
        {
            _pendingInput = input;
        }
    }

    /// <summary>
    /// Advance the camera by <paramref name="deltaTime"/> seconds using the
    /// most recently submitted input snapshot.
    /// Call this once per frame on the render thread, before reading
    /// <see cref="ViewMatrix"/>.
    /// </summary>
    public void Update(float deltaTime)
    {
        CameraInput input;
        lock (_inputLock)
        {
            input = _pendingInput;
        }

        if (!HasAnyInput(input))
        {
            return;
        }

        // Build the three orthonormal basis vectors from current yaw/pitch.
        Vector3 forward = ForwardVector();
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, WorldUp));
        // No need to recompute up — we always use WorldUp for vertical movement
        // so the camera doesn't roll.

        float distance = MoveSpeed * deltaTime;
        Vector3 delta = Vector3.Zero;

        if (input.MoveForward) delta += forward * distance;
        if (input.MoveBackward) delta -= forward * distance;
        if (input.StrafeRight) delta += right * distance;
        if (input.StrafeLeft) delta -= right * distance;
        if (input.MoveUp) delta += WorldUp * distance;
        if (input.MoveDown) delta -= WorldUp * distance;

        _position += delta;

        // Clamp position so the camera can't wander off to infinity.
        _position = Vector3.Clamp(
            _position,
            new Vector3(-MaxDistance),
            new Vector3(MaxDistance));

        RebuildViewMatrix();
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the normalised forward vector for the current yaw / pitch.
    /// Right-handed: +Z is "out of the screen" (away from the viewer),
    /// so a forward vector pointing into the scene has a negative Z component
    /// when yaw = 0.
    /// </summary>
    private Vector3 ForwardVector()
    {
        float cosPitch = MathF.Cos(_pitch);
        return Vector3.Normalize(new Vector3(
            cosPitch * MathF.Sin(_yaw),
            MathF.Sin(_pitch),
            cosPitch * MathF.Cos(_yaw)));
    }

    private void RebuildViewMatrix()
    {
        Vector3 forward = ForwardVector();
        _viewMatrix = Matrix4x4.CreateLookAt(_position, _position + forward, WorldUp);
    }

    private static bool HasAnyInput(CameraInput i) =>
        i.MoveForward || i.MoveBackward || i.StrafeLeft ||
        i.StrafeRight || i.MoveUp || i.MoveDown;
}