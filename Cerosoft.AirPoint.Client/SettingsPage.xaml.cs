namespace Cerosoft.AirPoint.Client
{
    // Changed inheritance from ContentPage to ContentView
    public partial class SettingsPage : ContentView
    {
        // --- GLOBAL SETTINGS (Static access preserved for TouchpadView) ---
        // Fix: Converted public static fields to Properties to resolve "Non-constant fields should not be visible"
        public static float CursorSensitivity { get; set; } = 1.5f;
        public static float ScrollSensitivity { get; set; } = 1.0f;
        public static float ZoomSensitivity { get; set; } = 1.0f;

        public static bool MasterGesturesEnabled { get; set; } = true;
        public static bool ScrollEnabled { get; set; } = true;
        public static bool ZoomEnabled { get; set; } = true;
        public static bool SwipeEnabled { get; set; } = true;
        public static bool DragEnabled { get; set; } = true;

        // Event to tell MainPage to close this view
        public event EventHandler? BackRequested;

        public SettingsPage()
        {
            InitializeComponent();
            LoadSettings();
            WireUpEvents();
        }

        private void LoadSettings()
        {
            CursorSensitivity = (float)Preferences.Get("CursorSensitivity", 1.5f);
            ScrollSensitivity = (float)Preferences.Get("ScrollSensitivity", 1.0f);
            ZoomSensitivity = (float)Preferences.Get("ZoomSensitivity", 1.0f);

            MasterGesturesEnabled = Preferences.Get("MasterGesturesEnabled", true);
            ScrollEnabled = Preferences.Get("ScrollEnabled", true);
            ZoomEnabled = Preferences.Get("ZoomEnabled", true);
            SwipeEnabled = Preferences.Get("SwipeEnabled", true);
            DragEnabled = Preferences.Get("DragEnabled", true);

            SensitivitySlider.Value = CursorSensitivity;
            ScrollSlider.Value = ScrollSensitivity;
            ZoomSlider.Value = ZoomSensitivity;

            MasterGestureSwitch.IsToggled = MasterGesturesEnabled;
            ScrollSwitch.IsToggled = ScrollEnabled;
            ZoomSwitch.IsToggled = ZoomEnabled;
            SwipeSwitch.IsToggled = SwipeEnabled;
            DragSwitch.IsToggled = DragEnabled;

            UpdateVisualState();
            UpdateLabels();
        }

        private void WireUpEvents()
        {
            SensitivitySlider.ValueChanged += (s, e) => { CursorSensitivity = (float)e.NewValue; Preferences.Set("CursorSensitivity", CursorSensitivity); UpdateLabels(); };
            ScrollSlider.ValueChanged += (s, e) => { ScrollSensitivity = (float)e.NewValue; Preferences.Set("ScrollSensitivity", ScrollSensitivity); UpdateLabels(); };
            ZoomSlider.ValueChanged += (s, e) => { ZoomSensitivity = (float)e.NewValue; Preferences.Set("ZoomSensitivity", ZoomSensitivity); UpdateLabels(); };

            MasterGestureSwitch.Toggled += (s, e) => {
                MasterGesturesEnabled = e.Value;
                Preferences.Set("MasterGesturesEnabled", MasterGesturesEnabled);
                UpdateVisualState();
            };

            ScrollSwitch.Toggled += (s, e) => { ScrollEnabled = e.Value; Preferences.Set("ScrollEnabled", ScrollEnabled); };
            ZoomSwitch.Toggled += (s, e) => { ZoomEnabled = e.Value; Preferences.Set("ZoomEnabled", ZoomEnabled); };
            SwipeSwitch.Toggled += (s, e) => { SwipeEnabled = e.Value; Preferences.Set("SwipeEnabled", SwipeEnabled); };
            DragSwitch.Toggled += (s, e) => { DragEnabled = e.Value; Preferences.Set("DragEnabled", DragEnabled); };
        }

        private void UpdateVisualState()
        {
            // Ensure IndividualGesturesLayout is defined in your XAML
            // Fix: Simplified null check using 'is not null'
            if (IndividualGesturesLayout is not null)
            {
                IndividualGesturesLayout.Opacity = MasterGesturesEnabled ? 1.0 : 0.4;
                IndividualGesturesLayout.IsEnabled = MasterGesturesEnabled;
            }
        }

        private void UpdateLabels()
        {
            // Ensure these Labels are defined in your XAML
            // Fix: Simplified null checks using 'is not null'
            if (CursorValLabel is not null) CursorValLabel.Text = $"{CursorSensitivity:F1}x";
            if (ScrollValLabel is not null) ScrollValLabel.Text = $"{ScrollSensitivity:F1}x";
            if (ZoomValLabel is not null) ZoomValLabel.Text = $"{ZoomSensitivity:F1}x";
        }

        // Trigger the event instead of Navigation.PopAsync
        private void OnBackClicked(object sender, EventArgs e)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}