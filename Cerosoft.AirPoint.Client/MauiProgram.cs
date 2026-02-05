using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;
using Cerosoft.AirPoint.Client;

#if ANDROID
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui.Platform;
#endif

namespace Cerosoft.AirPoint.Client
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseBarcodeReader()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            // --- PRECISION TOUCHPAD HANDLER ---
            Microsoft.Maui.Handlers.ElementHandler.ElementMapper.AppendToMapping("PrecisionTouchpad", (handler, view) =>
            {
#if ANDROID
                if (view is TouchpadView touchpadView && handler.PlatformView is Android.Views.View nativeView)
                {
                    nativeView.SetOnTouchListener(new PrecisionTouchListener(touchpadView));
                }
#endif
            });

            // --- FULLSCREEN LOGIC (API 35 COMPATIBLE) ---
            Microsoft.Maui.Handlers.PageHandler.Mapper.AppendToMapping(nameof(ContentPage), (handler, view) =>
            {
#if ANDROID
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity?.Window is not null)
                {
                    var window = activity.Window;
                    var controller = WindowCompat.GetInsetsController(window, window.DecorView);

                    if (controller is not null)
                    {
                        controller.Hide(WindowInsetsCompat.Type.SystemBars());
                        controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
                    }
                }
#endif
            });

            return builder.Build();
        }
    }

#if ANDROID
    public class PrecisionTouchListener(TouchpadView view) : Java.Lang.Object, Android.Views.View.IOnTouchListener
    {
        private readonly TouchpadView _mauiView = view;

        // --- CONSTANTS ---
        private const int TOUCH_SLOP = 12;
        private const int DRAG_LATCH_TIMEOUT_MS = 250;

        // Tuned for "Hold and Swipe" feel - higher threshold prevents accidental fires
        private const int SWIPE_THRESHOLD = 80;

        // --- STATE ENUMS ---
        private enum GestureMode { None, Moving, Dragging, Scrolling, Zooming, ThreeFingerGesturing }
        private GestureMode _currentMode = GestureMode.None;

        // --- TRACKING VARS ---
        private float _lastX, _lastY;
        private float _startX, _startY;
        private long _startTime;
        private int _activePointers = 0;
        private int _maxActivePointers = 0;

        // --- DRAG LOGIC ---
        private long _lastUpTime = 0;
        private bool _isPotentialDrag = false;

        // --- MULTI-TOUCH ---
        private float _initialPinchDist = 0;
        private float _lastPinchDist = 0;

        // --- 3-FINGER SWIPE LOGIC ---
        private float _swipeStartX, _swipeStartY;
        private bool _hasTriggeredSwipe = false;

        public bool OnTouch(Android.Views.View? v, MotionEvent? e)
        {
            if (e is null) return false;

            v?.Parent?.RequestDisallowInterceptTouchEvent(true);
            var action = e.ActionMasked;

            switch (action)
            {
                case MotionEventActions.Down:
                    _activePointers = 1;
                    _maxActivePointers = 1;
                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    _startX = _lastX;
                    _startY = _lastY;
                    _startTime = System.DateTime.Now.Ticks;

                    _currentMode = GestureMode.None;

                    // Smart Drag Detection
                    if (System.DateTime.Now.Ticks - _lastUpTime < (DRAG_LATCH_TIMEOUT_MS * 10000))
                    {
                        _isPotentialDrag = true;
                    }
                    else
                    {
                        _isPotentialDrag = false;
                    }
                    return true;

                case MotionEventActions.PointerDown:
                    _activePointers++;
                    if (_activePointers > _maxActivePointers) _maxActivePointers = _activePointers;

                    _lastX = GetFocusX(e);
                    _lastY = GetFocusY(e);
                    _isPotentialDrag = false;

                    if (_activePointers == 2)
                    {
                        _initialPinchDist = GetPinchDistance(e);
                        _lastPinchDist = _initialPinchDist;
                        _currentMode = GestureMode.None;
                    }
                    else if (_activePointers == 3)
                    {
                        // --- INITIALIZE 3-FINGER SWIPE ---
                        _swipeStartX = GetFocusX(e);
                        _swipeStartY = GetFocusY(e);
                        _hasTriggeredSwipe = false; // Reset trigger
                        _currentMode = GestureMode.ThreeFingerGesturing;
                    }
                    return true;

                case MotionEventActions.PointerUp:
                    _activePointers--;
                    _lastX = GetFocusX(e);
                    _lastY = GetFocusY(e);

                    if (_activePointers < 2 && (_currentMode == GestureMode.Scrolling || _currentMode == GestureMode.Zooming))
                    {
                        _currentMode = GestureMode.None;
                    }
                    // If we drop from 3 fingers, reset mode but don't fire swipe here (already fired in Move)
                    if (_activePointers < 3 && _currentMode == GestureMode.ThreeFingerGesturing)
                    {
                        _currentMode = GestureMode.None;
                    }
                    return true;

                case MotionEventActions.Move:
                    float currentX = GetFocusX(e);
                    float currentY = GetFocusY(e);
                    float deltaX = currentX - _lastX;
                    float deltaY = currentY - _lastY;

                    // --- 1 FINGER LOGIC ---
                    if (_activePointers == 1)
                    {
                        float totalMove = (float)Math.Sqrt(Math.Pow(currentX - _startX, 2) + Math.Pow(currentY - _startY, 2));

                        if (_currentMode == GestureMode.None && totalMove > TOUCH_SLOP)
                        {
                            if (_isPotentialDrag)
                            {
                                _currentMode = GestureMode.Dragging;
                                _mauiView.OnNativeDragStateChange(true);
                            }
                            else
                            {
                                _currentMode = GestureMode.Moving;
                            }
                        }

                        if (_currentMode == GestureMode.Moving)
                        {
                            _mauiView.OnNativeMove(deltaX, deltaY);
                        }
                        else if (_currentMode == GestureMode.Dragging)
                        {
                            _mauiView.OnNativeMove(deltaX, deltaY);
                        }
                    }
                    // --- 2 FINGER LOGIC ---
                    else if (_activePointers == 2)
                    {
                        float currentPinchDist = GetPinchDistance(e);
                        float pinchDelta = currentPinchDist - _initialPinchDist;
                        float scrollDelta = currentY - _startY;

                        if (_currentMode == GestureMode.None)
                        {
                            if (Math.Abs(pinchDelta) > 40 && Math.Abs(pinchDelta) > Math.Abs(scrollDelta) * 1.5)
                            {
                                _currentMode = GestureMode.Zooming;
                            }
                            else if (Math.Abs(scrollDelta) > 30)
                            {
                                _currentMode = GestureMode.Scrolling;
                            }
                        }

                        if (_currentMode == GestureMode.Zooming)
                        {
                            float scaleChange = currentPinchDist - _lastPinchDist;
                            float scaleFactor = 1.0f + (scaleChange / 500.0f);
                            _mauiView.OnNativeZoom(scaleFactor);
                            _lastPinchDist = currentPinchDist;
                        }
                        else if (_currentMode == GestureMode.Scrolling)
                        {
                            _mauiView.OnNativeScroll(deltaY);
                        }
                    }
                    // --- 3 FINGER HOLD & SWIPE LOGIC ---
                    else if (_activePointers == 3 && _currentMode == GestureMode.ThreeFingerGesturing)
                    {
                        // Calculate total distance moved from the *start* of the 3-finger hold
                        float totalSwipeX = currentX - _swipeStartX;
                        float totalSwipeY = currentY - _swipeStartY;

                        // Only fire if we haven't fired yet for this specific gesture interaction
                        if (!_hasTriggeredSwipe)
                        {
                            if (Math.Abs(totalSwipeY) > SWIPE_THRESHOLD)
                            {
                                // Vertical Swipe
                                _mauiView.OnNativeSwipe(totalSwipeY < 0 ? SwipeDirection.Up : SwipeDirection.Down);
                                _hasTriggeredSwipe = true; // Lock it so we don't spam commands
                            }
                            else if (Math.Abs(totalSwipeX) > SWIPE_THRESHOLD)
                            {
                                // Horizontal Swipe
                                _mauiView.OnNativeSwipe(totalSwipeX < 0 ? SwipeDirection.Left : SwipeDirection.Right);
                                _hasTriggeredSwipe = true;
                            }
                        }
                    }

                    _lastX = currentX;
                    _lastY = currentY;
                    return true;

                case MotionEventActions.Up:
                    if (_currentMode == GestureMode.Dragging)
                    {
                        _mauiView.OnNativeDragStateChange(false);
                        _isPotentialDrag = false;
                        _lastUpTime = 0;
                    }
                    else if (_currentMode == GestureMode.None && (System.DateTime.Now.Ticks - _startTime) < 2000000)
                    {
                        // Tap Handling (Left vs Right Click)
                        byte clickType = (_maxActivePointers == 2) ? (byte)3 : (byte)2;
                        _mauiView.OnNativeClick(clickType);
                    }

                    // Reset all
                    _activePointers = 0;
                    _lastUpTime = System.DateTime.Now.Ticks;
                    _currentMode = GestureMode.None;
                    _hasTriggeredSwipe = false;
                    return true;
            }
            return false;
        }

        private static float GetFocusX(MotionEvent e)
        {
            float sum = 0;
            int count = e.PointerCount;
            for (int i = 0; i < count; i++) sum += e.GetX(i);
            return sum / count;
        }

        private static float GetFocusY(MotionEvent e)
        {
            float sum = 0;
            int count = e.PointerCount;
            for (int i = 0; i < count; i++) sum += e.GetY(i);
            return sum / count;
        }

        private static float GetPinchDistance(MotionEvent e)
        {
            if (e.PointerCount < 2) return 0;
            float x = e.GetX(0) - e.GetX(1);
            float y = e.GetY(0) - e.GetY(1);
            return (float)Math.Sqrt(x * x + y * y);
        }
    }
#endif
}