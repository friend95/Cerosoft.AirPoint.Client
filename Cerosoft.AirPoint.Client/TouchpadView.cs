using System.ComponentModel;
using System.Runtime.CompilerServices; // Required for AggressiveInlining

namespace Cerosoft.AirPoint.Client;

/// <summary>
/// Defines the directional vectors for multi-touch swipe gestures.
/// </summary>
public enum SwipeDirection
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// A high-performance, lightweight surface for capturing raw native touch events.
/// This view acts as a bridge between the native platform Renderer/Handler and the .NET MAUI business logic.
/// </summary>
public class TouchpadView : BoxView
{
    // --- HOT PATH EVENTS (120Hz+ Capable) ---

    /// <summary>
    /// Fired when a single-finger pan is detected.
    /// Payload: Delta X, Delta Y.
    /// </summary>
    public event Action<float, float>? MoveReceived;

    /// <summary>
    /// Fired when a two-finger scroll is detected.
    /// Payload: Vertical Delta Y.
    /// </summary>
    public event Action<float>? ScrollReceived;

    /// <summary>
    /// Fired when a pinch/spread gesture is detected.
    /// Payload: Scale Factor Delta (e.g., 1.05 for 5% zoom in).
    /// </summary>
    public event Action<float>? ZoomReceived;

    // --- DISCRETE EVENTS ---

    /// <summary>
    /// Fired on tap.
    /// Payload: 2 = Left Click, 3 = Right Click.
    /// </summary>
    public event Action<byte>? ClickReceived;

    /// <summary>
    /// Fired when the drag latch state changes (e.g., tap-and-hold to drag).
    /// </summary>
    public event Action<bool>? DragStateChanged;

    /// <summary>
    /// Fired when a three-finger swipe is detected.
    /// </summary>
    public event Action<SwipeDirection>? SwipeReceived;

    // --- NATIVE INTEROP HANDLERS ---
    // These methods are called directly by the Android/iOS Platform Handlers.
    // We use AggressiveInlining to ensure the JIT eliminates the method call overhead,
    // essentially pasting the event invocation directly into the native loop.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void OnNativeMove(float x, float y) => MoveReceived?.Invoke(x, y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void OnNativeScroll(float y) => ScrollReceived?.Invoke(y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void OnNativeZoom(float scale) => ZoomReceived?.Invoke(scale);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void OnNativeClick(byte type) => ClickReceived?.Invoke(type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void OnNativeDragStateChange(bool isDragging) => DragStateChanged?.Invoke(isDragging);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void OnNativeSwipe(SwipeDirection direction) => SwipeReceived?.Invoke(direction);
}