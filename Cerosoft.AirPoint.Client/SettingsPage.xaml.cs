using System.Globalization;
using System.Runtime.CompilerServices; // For AggressiveInlining

// Modernization: File-scoped namespace (C# 10+)
namespace Cerosoft.AirPoint.Client;

// Industry Grade: Use ContentView for modular settings panels
public partial class SettingsPage : ContentView
{
    // --- CONSTANTS (Avoid Magic Strings) ---
    private const string KeyCursor = "CursorSensitivity";
    private const string KeyScroll = "ScrollSensitivity";
    private const string KeyZoom = "ZoomSensitivity";
    private const string KeyMaster = "MasterGesturesEnabled";
    private const string KeyScrollEnabled = "ScrollEnabled";
    private const string KeyZoomEnabled = "ZoomEnabled";
    private const string KeySwipeEnabled = "SwipeEnabled";
    private const string KeyDragEnabled = "DragEnabled";

    // --- GLOBAL STATE (Static Access for High-Perf Hot Paths) ---
    public static float CursorSensitivity { get; private set; } = 1.5f;
    public static float ScrollSensitivity { get; private set; } = 1.0f;
    public static float ZoomSensitivity { get; private set; } = 1.0f;

    // Feature Flags (Booleans are atomic, safe for concurrent read)
    public static bool MasterGesturesEnabled { get; private set; } = true;
    public static bool ScrollEnabled { get; private set; } = true;
    public static bool ZoomEnabled { get; private set; } = true;
    public static bool SwipeEnabled { get; private set; } = true;
    public static bool DragEnabled { get; private set; } = true;

    // Event to signal navigation request
    public event EventHandler? BackRequested;

    // Debounce Timer for Slider Persistence
    private readonly IDispatcherTimer _debounceTimer;
    private bool _isDirty;

    public SettingsPage()
    {
        InitializeComponent();

        // Initialize Debouncer (500ms write delay)
        _debounceTimer = Dispatcher.CreateTimer();
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(500);
        _debounceTimer.Tick += OnDebounceTick;

        LoadSettings();
        WireUpEvents();
    }

    private void LoadSettings()
    {
        // Batch Load to prevent UI jitter
        CursorSensitivity = GetFloat(KeyCursor, 1.5f);
        ScrollSensitivity = GetFloat(KeyScroll, 1.0f);
        ZoomSensitivity = GetFloat(KeyZoom, 1.0f);

        MasterGesturesEnabled = Preferences.Get(KeyMaster, true);
        ScrollEnabled = Preferences.Get(KeyScrollEnabled, true);
        ZoomEnabled = Preferences.Get(KeyZoomEnabled, true);
        SwipeEnabled = Preferences.Get(KeySwipeEnabled, true);
        DragEnabled = Preferences.Get(KeyDragEnabled, true);

        // Apply to UI (Atomic Update)
        // FIX IDE0031: Removed unnecessary 'if' checks. 
        // Controls are guaranteed to exist after InitializeComponent.
        // We use '!' to suppress the compiler's static null warning.
        SensitivitySlider!.Value = CursorSensitivity;
        ScrollSlider!.Value = ScrollSensitivity;
        ZoomSlider!.Value = ZoomSensitivity;

        MasterGestureSwitch!.IsToggled = MasterGesturesEnabled;
        ScrollSwitch!.IsToggled = ScrollEnabled;
        ZoomSwitch!.IsToggled = ZoomEnabled;
        SwipeSwitch!.IsToggled = SwipeEnabled;
        DragSwitch!.IsToggled = DragEnabled;

        UpdateVisualState();
        UpdateLabels();
    }

    private void WireUpEvents()
    {
        // Use Method Groups instead of Lambdas to reduce GC allocation
        // FIX IDE0031: Removed unnecessary 'if' checks.
        SensitivitySlider!.ValueChanged += OnSensitivityChanged;
        ScrollSlider!.ValueChanged += OnScrollSpeedChanged;
        ZoomSlider!.ValueChanged += OnZoomSpeedChanged;

        MasterGestureSwitch!.Toggled += OnMasterSwitchToggled;
        ScrollSwitch!.Toggled += OnScrollSwitchToggled;
        ZoomSwitch!.Toggled += OnZoomSwitchToggled;
        SwipeSwitch!.Toggled += OnSwipeSwitchToggled;
        DragSwitch!.Toggled += OnDragSwitchToggled;
    }

    // --- EVENT HANDLERS ---

    private void OnSensitivityChanged(object? sender, ValueChangedEventArgs e)
    {
        CursorSensitivity = (float)e.NewValue;
        UpdateLabels();
        QueueSave();
    }

    private void OnScrollSpeedChanged(object? sender, ValueChangedEventArgs e)
    {
        ScrollSensitivity = (float)e.NewValue;
        UpdateLabels();
        QueueSave();
    }

    private void OnZoomSpeedChanged(object? sender, ValueChangedEventArgs e)
    {
        ZoomSensitivity = (float)e.NewValue;
        UpdateLabels();
        QueueSave();
    }

    private void OnMasterSwitchToggled(object? sender, ToggledEventArgs e)
    {
        MasterGesturesEnabled = e.Value;
        Preferences.Set(KeyMaster, e.Value);
        UpdateVisualState();
    }

    private void OnScrollSwitchToggled(object? sender, ToggledEventArgs e)
    {
        ScrollEnabled = e.Value;
        Preferences.Set(KeyScrollEnabled, e.Value);
    }

    private void OnZoomSwitchToggled(object? sender, ToggledEventArgs e)
    {
        ZoomEnabled = e.Value;
        Preferences.Set(KeyZoomEnabled, e.Value);
    }

    private void OnSwipeSwitchToggled(object? sender, ToggledEventArgs e)
    {
        SwipeEnabled = e.Value;
        Preferences.Set(KeySwipeEnabled, e.Value);
    }

    private void OnDragSwitchToggled(object? sender, ToggledEventArgs e)
    {
        DragEnabled = e.Value;
        Preferences.Set(KeyDragEnabled, e.Value);
    }

    // --- UI UPDATES ---

    private void UpdateVisualState()
    {
        // FIX IDE0031: Removed null check. Layout is part of XAML.
        IndividualGesturesLayout!.Opacity = MasterGesturesEnabled ? 1.0 : 0.4;
        IndividualGesturesLayout.IsEnabled = MasterGesturesEnabled;
    }

    private void UpdateLabels()
    {
        // CultureInfo.InvariantCulture prevents crashes in regions using "," decimals (e.g. Europe)
        // FIX IDE0031: Removed unnecessary 'if' checks.
        CursorValLabel!.Text = string.Format(CultureInfo.InvariantCulture, "{0:F1}x", CursorSensitivity);
        ScrollValLabel!.Text = string.Format(CultureInfo.InvariantCulture, "{0:F1}x", ScrollSensitivity);
        ZoomValLabel!.Text = string.Format(CultureInfo.InvariantCulture, "{0:F1}x", ZoomSensitivity);
    }

    private void OnBackClicked(object sender, EventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    // --- HELPERS ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetFloat(string key, float def) => (float)Preferences.Get(key, def);

    // --- DEBOUNCER ---
    // Prevents writing to disk 60 times per second while dragging sliders
    private void QueueSave()
    {
        _isDirty = true;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        if (_isDirty)
        {
            Preferences.Set(KeyCursor, CursorSensitivity);
            Preferences.Set(KeyScroll, ScrollSensitivity);
            Preferences.Set(KeyZoom, ZoomSensitivity);
            _isDirty = false;
        }
    }
}