namespace DXtestWPF;

/// <summary>
/// A snapshot of which camera-movement keys are currently held down.
/// Produced by the WPF window layer and consumed by <see cref="Camera"/>.
/// This type deliberately has no dependency on WPF, Win32, or Direct3D so
/// that <see cref="Camera"/> remains fully portable and unit-testable.
/// </summary>
public readonly record struct CameraInput(
    bool MoveForward,   // W  or Arrow-Up
    bool MoveBackward,  // S  or Arrow-Down
    bool StrafeLeft,    // A  or Arrow-Left
    bool StrafeRight,   // D  or Arrow-Right
    bool MoveUp,        // E  (ascend along world-Y)
    bool MoveDown);     // Q  (descend along world-Y)