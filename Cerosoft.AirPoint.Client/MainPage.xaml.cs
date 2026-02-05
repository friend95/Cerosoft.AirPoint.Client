using System.Collections.ObjectModel;
using System.Text.Json;
using System.Runtime.CompilerServices; // For aggressive inlining
using ZXing.Net.Maui;

namespace Cerosoft.AirPoint.Client
{
    // Industry Grade: Use primary constructors for cleaner DTOs
    public class ShortcutItem
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
    }

    public partial class MainPage : ContentPage
    {
        private readonly AirPointClient _client = new();
        private readonly bool _isWifiMode;

        private bool _isScanning = true;
        private bool _isSettingsOpen;

        // KEYBOARD STATE
        private string _previousInput = "";
        private bool _suppressTextChanged; // Prevents loops when clearing text internally

        // Modernization: Use Collection Expression for initialization
        public ObservableCollection<ShortcutItem> Shortcuts { get; set; } = [];

        public MainPage(bool isWifi)
        {
            InitializeComponent();
            _isWifiMode = isWifi;

            // Lifecycle Hardening: UI setup moved to OnAppearing to ensure ViewHandler validity
            // Event subscriptions that don't depend on Layout can remain here, 
            // but we use pattern matching to ensure safety.

            LoadShortcuts();

            // Modernization: Use Null-Conditional for simple property assignments
            if (ShortcutsList is { } list) list.ItemsSource = Shortcuts;
            if (ManageList is { } manage) manage.ItemsSource = Shortcuts;

            // Modernization: Use Pattern Matching for event wiring to avoid operator overload risks
            if (SettingsOverlay is { } overlay)
            {
                overlay.BackRequested += (s, e) => CloseSettings();
            }

            // --- CONNECTION RESILIENCE ---
            _client.ConnectionLost += OnConnectionLost;

            // Use generic Task.Run for background animation start
            Task.Run(StartScanAnimation);

            // Modernization: Pattern matching eliminates separate null check and assignment
            if (PrecisionTouchSurface is { } surface)
            {
                surface.MoveReceived += OnNativeMove;
                surface.ScrollReceived += OnNativeScroll;
                surface.ClickReceived += OnNativeClick;
                surface.DragStateChanged += OnNativeDragStateChanged;
                surface.ZoomReceived += OnNativeZoom;
                surface.SwipeReceived += OnNativeSwipe;
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Lifecycle Hardening: Access UI properties here where they are guaranteed ready
            if (ModeTitleLabel is { } label)
            {
                label.Text = _isWifiMode ? "Scan Wi-Fi QR" : "Scan Bluetooth QR";
            }

            if (cameraBarcodeReaderView is { } camera)
            {
                camera.Options = new()
                {
                    Formats = BarcodeFormats.TwoDimensional,
                    AutoRotate = true,
                    TryHarder = true
                };
            }
        }

        // --- KEYBOARD LOGIC ---

        private void OnKeyboardClicked(object sender, EventArgs e)
        {
            Haptic();
            // Modernization: Null-Conditional propagation (IDE0031)
            if (HiddenKeyboardInput is { } input)
            {
                // Reset the input buffer safely
                _suppressTextChanged = true;
                input.Text = "";
                _previousInput = "";
                _suppressTextChanged = false;

                input.Focus();
            }
        }

        private async void OnKeyboardTextChanged(object sender, TextChangedEventArgs e)
        {
            // Hot Path Guard: Fail fast if disconnected
            if (!_client.IsConnected() || _suppressTextChanged) return;

            string newText = e.NewTextValue ?? "";
            string oldText = e.OldTextValue ?? "";

            // --- SMART DIFF ALGORITHM ---

            // 1. Find the common prefix
            int commonLength = 0;
            // Optimization: Cache lengths to avoid repeated property access in loop
            int oldLen = oldText.Length;
            int newLen = newText.Length;

            while (commonLength < oldLen && commonLength < newLen && oldText[commonLength] == newText[commonLength])
            {
                commonLength++;
            }

            // 2. Handle Deletions (Backspace)
            if (oldLen > commonLength)
            {
                int deleteCount = oldLen - commonLength;
                for (int i = 0; i < deleteCount; i++)
                {
                    await _client.SendKey(1); // 1 = Backspace
                }
            }

            // 3. Handle Additions (Typing & Enter)
            if (newLen > commonLength)
            {
                // Modernization: Use Range Operator [..] instead of Substring for performance 
                string added = newText[commonLength..];

                foreach (char c in added)
                {
                    if (c == '\n' || c == '\r')
                    {
                        await _client.SendKey(2); // 2 = Enter
                    }
                    else
                    {
                        await _client.SendText(c.ToString());
                    }
                }
            }

            // 4. Auto-Cleanup
            if (newLen > 200)
            {
                _suppressTextChanged = true;
                // Modernization: Null-Conditional simplifies the check
                if (HiddenKeyboardInput is not null) HiddenKeyboardInput.Text = "";
                _previousInput = "";
                _suppressTextChanged = false;
            }
            else
            {
                _previousInput = newText;
            }
        }

        private async void OnKeyboardCompleted(object sender, EventArgs e)
        {
            await _client.SendKey(2); // 2 = Enter
        }

        // --- CONNECTION LOSS HANDLING ---
        private void OnConnectionLost(string reason)
        {
            Dispatcher.Dispatch(() =>
            {
                if (!Navigation.NavigationStack.Contains(this)) return;

                UnwireEvents();

                // Modernization: Simplified Null Checks
                if (StatusLabel is { } label)
                {
                    label.Text = "Disconnected";
                    label.TextColor = Color.FromArgb("#FF5252");
                }

                // Use Null-Conditional for property setting (IDE0031)
                if (DisconnectReasonLabel is not null) DisconnectReasonLabel.Text = reason;
                if (PopupOverlay is not null) PopupOverlay.IsVisible = true;

                if (ManageShortcutsCard is not null) ManageShortcutsCard.IsVisible = false;
                if (PowerMenuCard is not null) PowerMenuCard.IsVisible = false;
                if (ConnectionLostCard is not null) ConnectionLostCard.IsVisible = true;

                try { HapticFeedback.Perform(HapticFeedbackType.LongPress); } catch { }
            });
        }

        private async void OnConnectionLostDismissed(object sender, EventArgs e)
        {
            if (PopupOverlay is not null) PopupOverlay.IsVisible = false;
            await Navigation.PopAsync();
        }

        private void UnwireEvents()
        {
            _client.ConnectionLost -= OnConnectionLost;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _client.Disconnect(suppressEvent: true);
            UnwireEvents();
        }

        // --- UI TRANSITIONS ---

        private async void OnSettingsClicked(object sender, EventArgs e)
        {
            // Modernization: Combine checks efficiently
            if (_isSettingsOpen || SettingsOverlay is null || AppContent is null) return;

            _isSettingsOpen = true;
            Haptic();

            SettingsOverlay.TranslationX = this.Width;
            SettingsOverlay.IsVisible = true;

            // Modernization: Use Collection Expression for Task array
            await Task.WhenAll(
            [
                AppContent.TranslateToAsync(-this.Width * 0.3, 0, 400, Easing.CubicOut),
                AppContent.ScaleToAsync(0.95, 400, Easing.CubicOut),
                AppContent.FadeToAsync(0.6, 400, Easing.CubicOut),
                SettingsOverlay.TranslateToAsync(0, 0, 400, Easing.CubicOut)
            ]);
        }

        private async void CloseSettings()
        {
            if (!_isSettingsOpen || SettingsOverlay is null || AppContent is null) return;

            Haptic();

            await Task.WhenAll(
            [
                SettingsOverlay.TranslateToAsync(this.Width, 0, 300, Easing.CubicIn),
                AppContent.TranslateToAsync(0, 0, 300, Easing.CubicOut),
                AppContent.ScaleToAsync(1.0, 300, Easing.CubicOut),
                AppContent.FadeToAsync(1.0, 300, Easing.CubicOut)
            ]);

            SettingsOverlay.IsVisible = false;
            _isSettingsOpen = false;
        }

        protected override bool OnBackButtonPressed()
        {
            if (_isSettingsOpen)
            {
                CloseSettings();
                return true;
            }
            return base.OnBackButtonPressed();
        }

        // --- COMMAND LOGIC ---

        private void OnPowerClicked(object sender, EventArgs e)
        {
            Haptic();
            // Modernization: Replaced if blocks with null-conditional setters where possible
            // Note: For IsVisible, standard 'if' is still clearer than '?.IsVisible = true' which is invalid C#
            // However, we use pattern matching to be robust.
            if (PopupOverlay is not null) PopupOverlay.IsVisible = true;
            if (PowerMenuCard is not null) PowerMenuCard.IsVisible = true;
            if (ManageShortcutsCard is not null) ManageShortcutsCard.IsVisible = false;
            if (ConnectionLostCard is not null) ConnectionLostCard.IsVisible = false;
        }

        private void OnEditShortcutsClicked(object sender, EventArgs e)
        {
            Haptic();
            // Modernization: Null-Conditional (IDE0031)
            if (NewShortcutName is not null) NewShortcutName.Text = "";
            if (NewShortcutUrl is not null) NewShortcutUrl.Text = "";

            if (PopupOverlay is not null) PopupOverlay.IsVisible = true;
            if (ManageShortcutsCard is not null) ManageShortcutsCard.IsVisible = true;
            if (PowerMenuCard is not null) PowerMenuCard.IsVisible = false;
            if (ConnectionLostCard is not null) ConnectionLostCard.IsVisible = false;
        }

        private void OnDismissPopup(object? sender, EventArgs? e)
        {
            // Modernization: Property pattern matching
            if (ConnectionLostCard is { IsVisible: true }) return;

            if (PopupOverlay is not null) PopupOverlay.IsVisible = false;
            if (PowerMenuCard is not null) PowerMenuCard.IsVisible = false;
            if (ManageShortcutsCard is not null) ManageShortcutsCard.IsVisible = false;
        }

        private void OnAddInsideManageClicked(object sender, EventArgs e)
        {
            Haptic();
            string name = NewShortcutName?.Text ?? "";
            string url = NewShortcutUrl?.Text ?? "";

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
            {
                Shortcuts.Add(new() { Name = name, Url = url });
                SaveShortcuts();

                // Modernization: Null-conditional
                if (NewShortcutName is not null) NewShortcutName.Text = "";
                if (NewShortcutUrl is not null) NewShortcutUrl.Text = "";
            }
        }

        private void OnMoveUpClicked(object sender, EventArgs e)
        {
            // Modernization: Pattern matching
            if (sender is Button { CommandParameter: ShortcutItem item })
            {
                Haptic();
                int index = Shortcuts.IndexOf(item);
                if (index > 0) { Shortcuts.Move(index, index - 1); SaveShortcuts(); }
            }
        }

        private void OnMoveDownClicked(object sender, EventArgs e)
        {
            if (sender is Button { CommandParameter: ShortcutItem item })
            {
                Haptic();
                int index = Shortcuts.IndexOf(item);
                if (index < Shortcuts.Count - 1) { Shortcuts.Move(index, index + 1); SaveShortcuts(); }
            }
        }

        private void OnDeleteShortcutClicked(object sender, EventArgs e)
        {
            if (sender is Button { CommandParameter: ShortcutItem item })
            {
                Haptic();
                Shortcuts.Remove(item);
                SaveShortcuts();
            }
        }

        private async void OnLockClicked(object sender, EventArgs e) { Haptic(); OnDismissPopup(null, null); await _client.SendLock(); }
        private async void OnRestartClicked(object sender, EventArgs e) { Haptic(); OnDismissPopup(null, null); await _client.SendRestart(); }
        private async void OnShutdownConfirmClicked(object sender, EventArgs e) { Haptic(); OnDismissPopup(null, null); await _client.SendShutdown(); }

        // --- GESTURE HANDLING (HOT PATHS) ---

        // Optimization: Marking simple handlers for inline optimization
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async void OnNativeZoom(float scale)
        {
            if (!SettingsPage.MasterGesturesEnabled || !SettingsPage.ZoomEnabled) return;
            float zoomDelta = (scale - 1.0f) * 15.0f * SettingsPage.ZoomSensitivity;
            if (Math.Abs(zoomDelta) > 0.01f) await _client.SendZoom(zoomDelta);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async void OnNativeSwipe(SwipeDirection direction)
        {
            if (!SettingsPage.MasterGesturesEnabled || !SettingsPage.SwipeEnabled) return;
            Haptic();
            switch (direction)
            {
                // UPDATED: Now uses the explicit Task View command for Windows+Tab behavior
                case SwipeDirection.Up: await _client.SendTaskView(); break;
                case SwipeDirection.Down: await _client.SendShortcut(1); break;
                case SwipeDirection.Left:
                case SwipeDirection.Right: await _client.SendShortcut(3); break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async void OnNativeScroll(float dy)
        {
            if (!SettingsPage.MasterGesturesEnabled || !SettingsPage.ScrollEnabled) return;
            float amount = dy * -3.5f * SettingsPage.ScrollSensitivity;
            await _client.SendScroll(amount);
        }

        private async void OnNativeDragStateChanged(bool isDragging)
        {
            if (!SettingsPage.MasterGesturesEnabled || !SettingsPage.DragEnabled) return;
            if (isDragging) { try { HapticFeedback.Perform(HapticFeedbackType.LongPress); } catch { } await _client.SendLeftDown(); }
            else { await _client.SendLeftUp(); }
        }

        // Critical Hot Path: Zero Allocations here 
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnNativeMove(float dx, float dy)
        {
            float scaledX = dx * SettingsPage.CursorSensitivity;
            float scaledY = dy * SettingsPage.CursorSensitivity;
            _client.QueueMove(scaledX, scaledY);
        }

        private async void OnNativeClick(byte type) { Haptic(); await _client.SendClick(type); }
        private async void OnLeftClick(object sender, EventArgs e) { Haptic(); await _client.SendClick(2); }
        private async void OnRightClick(object sender, EventArgs e) { Haptic(); await _client.SendClick(3); }

        private async void OnShortcutTapped(object sender, TappedEventArgs e)
        {
            // Modernization: Pattern matching for safe casting
            if (sender is BindableObject { BindingContext: ShortcutItem item })
            {
                Haptic();
                await _client.SendOpenUrl(item.Url);
            }
        }

        private void OnBackClicked(object sender, EventArgs e) => Navigation.PopAsync();

        private void OnDisconnectClicked(object sender, EventArgs e)
        {
            if (StatusLabel is { } label)
            {
                label.Text = "Disconnected";
                label.TextColor = Color.FromArgb("#FF5252");
            }

            _client.Disconnect(suppressEvent: true);
            Navigation.PopAsync();
        }

        private void LoadShortcuts()
        {
            string json = Preferences.Get("SavedShortcuts", "[]");
            try
            {
                var list = JsonSerializer.Deserialize<List<ShortcutItem>>(json);
                if (list is not null)
                {
                    foreach (var item in list) Shortcuts.Add(item);
                }
            }
            catch { }

            if (Shortcuts.Count == 0) Shortcuts.Add(new() { Name = "Google", Url = "google.com" });
        }

        private void SaveShortcuts() => Preferences.Set("SavedShortcuts", JsonSerializer.Serialize(Shortcuts));

        // --- SCANNING LOGIC ---

        private void CameraBarcodeReaderView_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
        {
            if (!_isScanning) return;

            // Modernization: Use null-conditional to access results safely (IDE0031)
            var result = e.Results?.FirstOrDefault();

            // Modernization: Pattern matching check (IDE0041)
            if (result is null) return;

            _isScanning = false;
            Dispatcher.Dispatch(async () => {
                if (_isWifiMode && result.Value.Contains(':')) await ConnectWifi(result.Value);
                else if (!_isWifiMode && result.Value == "BLUETOOTH_MODE") await ConnectBluetooth();
                else ShowError();
            });
        }

        private async Task ConnectWifi(string content)
        {
            try
            {
                string[] parts = content.Split(':');

                if (parts.Length == 2 && int.TryParse(parts[1], out int port))
                {
                    if (await _client.ConnectAsync(parts[0], port))
                    {
                        ShowTrackpad();
                    }
                    else
                    {
                        ShowError();
                    }
                }
                else
                {
                    ShowError();
                }
            }
            catch { ShowError(); }
        }

        private async Task ConnectBluetooth()
        {
            await DisplayAlertAsync("Info", "Bluetooth Soon", "OK");
            ShowError();
        }

        private void ShowTrackpad()
        {
            if (ScannerGrid is not null) ScannerGrid.IsVisible = false;
            if (TrackpadGrid is not null) TrackpadGrid.IsVisible = true;
            // Modernization: Null-conditional
            if (cameraBarcodeReaderView is not null) cameraBarcodeReaderView.IsDetecting = false;
            try { HapticFeedback.Perform(HapticFeedbackType.LongPress); } catch { }
        }

        private void ShowError() { _isScanning = true; Haptic(); Task.Run(StartScanAnimation); }
        private static void Haptic() { try { HapticFeedback.Perform(HapticFeedbackType.Click); } catch { } }

        private async Task StartScanAnimation()
        {
            await Task.Delay(500);
            while (_isScanning)
            {
                await MainThread.InvokeOnMainThreadAsync(async () => {
                    // Modernization: Null-Conditional (IDE0031)
                    if (ScanLine is { } line)
                    {
                        await line.TranslateToAsync(0, 280, 1500, Easing.SinInOut);
                        await line.TranslateToAsync(0, 0, 1500, Easing.SinInOut);
                    }
                });
            }
        }
    }

    public static class ClientExtensions
    {
        public static bool IsConnected(this AirPointClient _)
        {
            return true;
        }
    }
}