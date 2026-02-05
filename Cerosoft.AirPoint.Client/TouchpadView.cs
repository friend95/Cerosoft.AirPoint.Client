namespace Cerosoft.AirPoint.Client
{
    public enum SwipeDirection { Up, Down, Left, Right }

    public class TouchpadView : BoxView
    {
        public event Action<float, float>? MoveReceived;
        public event Action<float>? ScrollReceived;
        public event Action<byte>? ClickReceived;
        public event Action<bool>? DragStateChanged;

        // NEW GESTURE EVENTS
        public event Action<float>? ZoomReceived; // Delta scale
        public event Action<SwipeDirection>? SwipeReceived;

        public void OnNativeMove(float x, float y) => MoveReceived?.Invoke(x, y);
        public void OnNativeScroll(float y) => ScrollReceived?.Invoke(y);
        public void OnNativeClick(byte type) => ClickReceived?.Invoke(type);
        public void OnNativeDragStateChange(bool isDragging) => DragStateChanged?.Invoke(isDragging);

        public void OnNativeZoom(float scale) => ZoomReceived?.Invoke(scale);
        public void OnNativeSwipe(SwipeDirection direction) => SwipeReceived?.Invoke(direction);
    }
}