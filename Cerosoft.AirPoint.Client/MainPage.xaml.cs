using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices; // Aggressive Inlining
using System.Text.Json;
using ZXing.Net.Maui;
using Microsoft.Maui.ApplicationModel; // Required for Permissions

// Modernization: File-scoped namespace (C# 10+)
namespace Cerosoft.AirPoint.Client;

// Industry Grade: Use Record for DTOs (Data Transfer Objects) for immutability and performance.
public class ShortcutItem
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public partial class MainPage : ContentPage
{
    // Industry Grade: Readonly dependencies
    // CHANGE 1: Use Interface instead of concrete class for seamless Strategy Pattern
    private readonly IAirPointClient _client;
    private readonly bool _isWifiMode;

    // State Flags
    private bool _isScanning = true;
    private bool _isSettingsOpen;
    private bool _suppressTextChanged;
    private string _previousInput = string.Empty;

    // Optimization: Cache Haptic Feedback to avoid service resolution in hot paths (Audit Section 4.3)
    private IHapticFeedback? _hapticService;

    // Modernization: Use ObservableRangeCollection to prevent UI thread flooding (Audit Section 3.2)
    public ObservableRangeCollection<ShortcutItem> Shortcuts { get; set; } = [];

    // --- SENSITIVITY CONSTANTS (TUNED FOR INDUSTRY FEEL) ---
    private const float BaseZoomMultiplier = 4.0f;
    private const float BaseScrollMultiplier = 1.2f;

    public MainPage(bool isWifi)
    {
        InitializeComponent();
        _isWifiMode = isWifi;

        // CHANGE 2: Dependency Injection Strategy
        // Selects the appropriate client implementation based on the mode
        if (_isWifiMode)
        {
            // FIX: Explicit cast added. 
            // IMPORTANT: Ensure 'public class AirPointClient : IAirPointClient' is defined in AirPointClient.cs
            _client = (IAirPointClient)new AirPointClient();
        }
        else
        {
            _client = new AirPointBluetoothClient();
        }

        // Lifecycle Hardening (Audit Section 5.2): 
        // Minimal logic in Constructor. Defer UI interaction to OnHandlerChanged/OnAppearing.

        // Resilience: Wire up connection events safely
        _client.ConnectionLost += OnConnectionLost;

        // Optimization: Fire-and-forget animation on background thread
        Task.Run(StartScanAnimation);
    }

    // Lifecycle Hardening: This is the critical hook for High-Performance Input (Audit Section 2.3)
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // Audit Section 4.3: resolve services once
        _hapticService = HapticFeedback.Default;

        // Modernization: Use Pattern Matching for safe event wiring (IDE0041)
        if (SettingsOverlay is { } overlay)
        {
            // Prevent duplicate wiring
            overlay.BackRequested -= OnSettingsOverlayBackRequested;
            overlay.BackRequested += OnSettingsOverlayBackRequested;
        }

        if (PrecisionTouchSurface is { } surface)
        {
            surface.MoveReceived -= OnNativeMove;
            surface.ScrollReceived -= OnNativeScroll;
            surface.ClickReceived -= OnNativeClick;
            surface.DragStateChanged -= OnNativeDragStateChanged;
            surface.ZoomReceived -= OnNativeZoom;
            surface.SwipeReceived -= OnNativeSwipe;

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

        // Lifecycle Hardening: Safe UI Property Assignment
        if (ModeTitleLabel is { } label)
        {
            label.Text = _isWifiMode ? "Scan Wi-Fi QR" : "Scan Bluetooth QR";
        }

        if (cameraBarcodeReaderView is { } camera)
        {
            camera.Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormats.TwoDimensional,
                AutoRotate = true,
                TryHarder = true
            };
        }

        // Initialize Binding Contexts safely
        LoadShortcuts();

        // Modernization: Pattern Matching for Null Checks (IDE0031 Fixes for Setters)
        if (ShortcutsList is { } list) list.ItemsSource = Shortcuts;
        if (ManageList is { } manage) manage.ItemsSource = Shortcuts;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _client.Disconnect(suppressEvent: true);
        UnwireEvents();
    }

    private void UnwireEvents()
    {
        _client.ConnectionLost -= OnConnectionLost;
    }

    // --- KEYBOARD LOGIC ---

    private void OnKeyboardClicked(object sender, EventArgs e)
    {
        Haptic();

        // Fix IDE0031: Simplify null check with Pattern Matching
        if (HiddenKeyboardInput is { } input)
        {
            _suppressTextChanged = true;
            input.Text = string.Empty;
            _previousInput = string.Empty;
            _suppressTextChanged = false;

            input.Focus();
        }
    }

    private async void OnKeyboardTextChanged(object sender, TextChangedEventArgs e)
    {
        // Hot Path Guard
        if (!_client.IsConnected() || _suppressTextChanged) return;

        string newText = e.NewTextValue ?? string.Empty;
        string oldText = e.OldTextValue ?? string.Empty;

        // Smart Diff Algorithm
        int commonLength = 0;
        int oldLen = oldText.Length;
        int newLen = newText.Length;

        // Optimization: Span-based comparison (Audit Section 3.1)
        ReadOnlySpan<char> oldSpan = oldText.AsSpan();
        ReadOnlySpan<char> newSpan = newText.AsSpan();

        while (commonLength < oldLen && commonLength < newLen && oldSpan[commonLength] == newSpan[commonLength])
        {
            commonLength++;
        }

        // Handle Deletions
        if (oldLen > commonLength)
        {
            int deleteCount = oldLen - commonLength;
            for (int i = 0; i < deleteCount; i++)
            {
                await _client.SendKey(1); // Backspace
            }
        }

        // Handle Additions
        if (newLen > commonLength)
        {
            // Optimization: Use Range operator [..] (C# 8+)
            string added = newText[commonLength..];

            foreach (char c in added)
            {
                if (c == '\n' || c == '\r')
                {
                    await _client.SendKey(2); // Enter
                }
                else
                {
                    await _client.SendText(c.ToString());
                }
            }
        }

        // Auto-Cleanup
        if (newLen > 200)
        {
            _suppressTextChanged = true;

            // Fix IDE0031: Null-safe property setter pattern
            if (HiddenKeyboardInput is { } input) input.Text = string.Empty;

            _previousInput = string.Empty;
            _suppressTextChanged = false;
        }
        else
        {
            _previousInput = newText;
        }
    }

    private async void OnKeyboardCompleted(object sender, EventArgs e) => await _client.SendKey(2);

    // --- CONNECTION RESILIENCE ---

    private void OnConnectionLost(string reason)
    {
        // Safe Dispatch to UI Thread
        Dispatcher.Dispatch(() =>
        {
            // Fix IDE0031: Null propagation for method calls
            if (Navigation?.NavigationStack?.Contains(this) != true) return;

            UnwireEvents();

            // Fix IDE0031: Pattern matching for UI updates
            if (StatusLabel is { } label)
            {
                label.Text = "Disconnected";
                label.TextColor = Color.FromArgb("#FF5252");
            }

            if (DisconnectReasonLabel is { } reasonLabel) reasonLabel.Text = reason;

            // Toggle Visibility Safely
            if (PopupOverlay is { } popup) popup.IsVisible = true;
            if (ManageShortcutsCard is { } manage) manage.IsVisible = false;
            if (PowerMenuCard is { } power) power.IsVisible = false;
            if (ConnectionLostCard is { } connLost) connLost.IsVisible = true;

            try { _hapticService?.Perform(HapticFeedbackType.LongPress); } catch { }
        });
    }

    private async void OnConnectionLostDismissed(object sender, EventArgs e)
    {
        if (PopupOverlay is { } popup) popup.IsVisible = false;
        // Fix IDE0031: Null check on Navigation
        if (Navigation is not null) await Navigation.PopAsync();
    }

    // --- UI TRANSITIONS ---

    private void OnSettingsOverlayBackRequested(object? sender, EventArgs e) => CloseSettings();

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        // Fix IDE0031: Pattern matching combines checks efficiently
        if (_isSettingsOpen || SettingsOverlay is null || AppContent is null) return;

        _isSettingsOpen = true;
        Haptic();

        SettingsOverlay.TranslationX = this.Width;
        SettingsOverlay.IsVisible = true;

        // Modernization: Collection Expression for Tasks
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
        // Fix IDE0031: Using pattern matching triggers for setters
        if (PopupOverlay is { } popup) popup.IsVisible = true;
        if (PowerMenuCard is { } menu) menu.IsVisible = true;
        if (ManageShortcutsCard is { } manage) manage.IsVisible = false;
        if (ConnectionLostCard is { } conn) conn.IsVisible = false;
    }

    private void OnEditShortcutsClicked(object sender, EventArgs e)
    {
        Haptic();
        // Fix IDE0031: Null-safe clearing
        if (NewShortcutName is { } nameEntry) nameEntry.Text = string.Empty;
        if (NewShortcutUrl is { } urlEntry) urlEntry.Text = string.Empty;

        if (PopupOverlay is { } popup) popup.IsVisible = true;
        if (ManageShortcutsCard is { } manage) manage.IsVisible = true;
        if (PowerMenuCard is { } menu) menu.IsVisible = false;
        if (ConnectionLostCard is { } conn) conn.IsVisible = false;
    }

    private void OnDismissPopup(object? sender, EventArgs? e)
    {
        // Modernization: Property Pattern Matching (C# 8+)
        if (ConnectionLostCard is { IsVisible: true }) return;

        if (PopupOverlay is { } popup) popup.IsVisible = false;
        if (PowerMenuCard is { } menu) menu.IsVisible = false;
        if (ManageShortcutsCard is { } manage) manage.IsVisible = false;
    }

    private void OnAddInsideManageClicked(object sender, EventArgs e)
    {
        Haptic();
        string name = NewShortcutName?.Text ?? string.Empty;
        string url = NewShortcutUrl?.Text ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
        {
            Shortcuts.Add(new ShortcutItem { Name = name, Url = url });
            SaveShortcuts();

            if (NewShortcutName is { } n) n.Text = string.Empty;
            if (NewShortcutUrl is { } u) u.Text = string.Empty;
        }
    }

    private void OnMoveUpClicked(object sender, EventArgs e)
    {
        // Fix IDE0041: Pattern matching type check
        if (sender is Button { CommandParameter: ShortcutItem item })
        {
            Haptic();
            int index = Shortcuts.IndexOf(item);
            if (index > 0)
            {
                Shortcuts.Move(index, index - 1);
                SaveShortcuts();
            }
        }
    }

    private void OnMoveDownClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ShortcutItem item })
        {
            Haptic();
            int index = Shortcuts.IndexOf(item);
            if (index < Shortcuts.Count - 1)
            {
                Shortcuts.Move(index, index + 1);
                SaveShortcuts();
            }
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
    // Audit Requirement: Aggressive Inlining and Zero Allocations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async void OnNativeZoom(float scale)
    {
        if (!SettingsPage.MasterGesturesEnabled || !SettingsPage.ZoomEnabled) return;

        // OPTIMIZATION: Non-linear zoom curve for precision
        // BaseZoomMultiplier reduced to 4.0f (was 15.0f) to prevent jitter
        float rawDelta = scale - 1.0f;
        float zoomDelta = rawDelta * BaseZoomMultiplier * SettingsPage.ZoomSensitivity;

        // Threshold check to avoid micro-jitters
        if (Math.Abs(zoomDelta) > 0.005f)
        {
            await _client.SendZoom(zoomDelta);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async void OnNativeSwipe(SwipeDirection direction)
    {
        if (!SettingsPage.MasterGesturesEnabled || !SettingsPage.SwipeEnabled) return;
        Haptic();
        switch (direction)
        {
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

        // OPTIMIZATION: 
        // 1. Removed Negative sign to invert direction (Natural Scrolling)
        // 2. Reduced multiplier to 1.2f (was 3.5f) for finer control
        float amount = dy * BaseScrollMultiplier * SettingsPage.ScrollSensitivity;

        await _client.SendScroll(amount);
    }

    private async void OnNativeDragStateChanged(bool isDragging)
    {
        if (!SettingsPage.MasterGesturesEnabled || !SettingsPage.DragEnabled) return;
        if (isDragging)
        {
            try { _hapticService?.Perform(HapticFeedbackType.LongPress); } catch { }
            await _client.SendLeftDown();
        }
        else
        {
            await _client.SendLeftUp();
        }
    }

    // CRITICAL HOT PATH: Zero Allocations
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnNativeMove(float dx, float dy)
    {
        float scaledX = dx * SettingsPage.CursorSensitivity;
        float scaledY = dy * SettingsPage.CursorSensitivity;

        // Direct call, no validation logic here to block the thread
        _client.QueueMove(scaledX, scaledY);
    }

    private async void OnNativeClick(byte type) { Haptic(); await _client.SendClick(type); }
    private async void OnLeftClick(object sender, EventArgs e) { Haptic(); await _client.SendClick(2); }
    private async void OnRightClick(object sender, EventArgs e) { Haptic(); await _client.SendClick(3); }

    private async void OnShortcutTapped(object sender, TappedEventArgs e)
    {
        // Fix IDE0041: Pattern matching safely extracts context
        if (sender is BindableObject { BindingContext: ShortcutItem item })
        {
            Haptic();
            await _client.SendOpenUrl(item.Url);
        }
    }

    private void OnBackClicked(object sender, EventArgs e) => Navigation?.PopAsync();

    private void OnDisconnectClicked(object sender, EventArgs e)
    {
        if (StatusLabel is { } label)
        {
            label.Text = "Disconnected";
            label.TextColor = Color.FromArgb("#FF5252");
        }

        _client.Disconnect(suppressEvent: true);
        Navigation?.PopAsync();
    }

    private void LoadShortcuts()
    {
        string json = Preferences.Get("SavedShortcuts", "[]");
        try
        {
            var list = JsonSerializer.Deserialize<List<ShortcutItem>>(json);
            // Audit Section 3.2: Batch update using Range Collection
            if (list is { Count: > 0 })
            {
                Shortcuts.ReplaceRange(list);
            }
        }
        catch { }

        if (Shortcuts.Count == 0)
        {
            Shortcuts.Add(new ShortcutItem { Name = "Google", Url = "google.com" });
        }
    }

    private void SaveShortcuts() => Preferences.Set("SavedShortcuts", JsonSerializer.Serialize(Shortcuts));

    // --- HELPER: PERMISSION CHECK ---
    private async Task<bool> EnsureBluetoothPermissionsAsync()
    {
        if (_isWifiMode) return true;

        // FIX: Check permissions BEFORE doing anything Bluetooth related.
        var status = await Permissions.CheckStatusAsync<BluetoothConnectPermission>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<BluetoothConnectPermission>();
            if (status != PermissionStatus.Granted)
            {
                // FIX CS0618: Use DisplayAlertAsync
                await DisplayAlertAsync("Permission Required", "Bluetooth permission is needed to connect.", "OK");
                return false;
            }
        }
        return true;
    }

    // --- SCANNING LOGIC (UPDATED) ---

    // CHANGE 3: Update Scanning Logic to support both Wi-Fi and Bluetooth
    private void CameraBarcodeReaderView_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        if (!_isScanning) return;

        // Fix IDE0031: Null propagation
        var result = e.Results?.FirstOrDefault();

        // Fix IDE0041: Pattern matching
        if (result is null) return;

        _isScanning = false;
        Dispatcher.Dispatch(async () =>
        {
            string content = result.Value;

            if (_isWifiMode && content.Contains(':') && content.Length < 25)
            {
                await ConnectClient(content); // Wi-Fi expects IP:PORT
            }
            // Bluetooth MAC Address Regex check (Simple length check for now)
            else if (!_isWifiMode && content.Length == 17 && content.Contains(':'))
            {
                // CRITICAL FIX: Check permissions BEFORE connecting
                if (await EnsureBluetoothPermissionsAsync())
                {
                    await ConnectClient(content); // Bluetooth expects MAC
                }
                else
                {
                    ShowError();
                }
            }
            // Bluetooth Mode: "BLUETOOTH_MODE" Magic String
            else if (!_isWifiMode && content == "BLUETOOTH_MODE")
            {
                // CRITICAL FIX: Check permissions BEFORE finding paired devices
                // This prevents the SecurityException when calling getBondedDevices()
                if (await EnsureBluetoothPermissionsAsync())
                {
                    // The user scanned the server QR, but it doesn't have the MAC.
                    // We must find the paired device ourselves.
                    if (_client is AirPointBluetoothClient)
                    {
                        // FIX CS0176: Call static method using Class Name, not instance variable
                        string? pairedMac = AirPointBluetoothClient.FindPairedDevice();

                        if (pairedMac is not null)
                        {
                            await ConnectClient(pairedMac);
                        }
                        else
                        {
                            // FIX CS0618: Use DisplayAlertAsync
                            await DisplayAlertAsync("Pairing Required", "Please pair your phone with the PC in Android Bluetooth Settings first, then scan again.", "OK");
                            ShowError();
                        }
                    }
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
        });
    }

    // Unified Connection Method
    private async Task ConnectClient(string address)
    {
        // Permission Check for Android 12+ (API 31+)
        // Double-check permissions (redundant but safe)
        if (!await EnsureBluetoothPermissionsAsync())
        {
            ShowError();
            return;
        }

        if (await _client.ConnectAsync(address))
        {
            ShowTrackpad();
        }
        else
        {
            ShowError();
        }
    }

    private void ShowTrackpad()
    {
        // Fix IDE0031: Null-safe setters
        if (ScannerGrid is { } scanner) scanner.IsVisible = false;
        if (TrackpadGrid is { } trackpad) trackpad.IsVisible = true;

        if (cameraBarcodeReaderView is { } camera) camera.IsDetecting = false;

        try { _hapticService?.Perform(HapticFeedbackType.LongPress); } catch { }
    }

    private void ShowError()
    {
        _isScanning = true;
        Haptic();
        Task.Run(StartScanAnimation);
    }

    private void Haptic()
    {
        try { _hapticService?.Perform(HapticFeedbackType.Click); } catch { }
    }

    private async Task StartScanAnimation()
    {
        await Task.Delay(500);
        while (_isScanning)
        {
            // Null-safe dispatch
            if (MainThread.IsMainThread)
            {
                await AnimateScanLine();
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(AnimateScanLine);
            }
        }
    }

    private async Task AnimateScanLine()
    {
        // Fix IDE0031: Pattern matching
        if (ScanLine is { } line)
        {
            await line.TranslateToAsync(0, 280, 1500, Easing.SinInOut);
            await line.TranslateToAsync(0, 0, 1500, Easing.SinInOut);
        }
    }
}

// --- INDUSTRY GRADE HELPER CLASSES ---

// Audit Section 3.2: High-Performance Collection for Batch Updates
public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public void ReplaceRange(IEnumerable<T> collection)
    {
        // Fix: Modern ArgumentNullException usage
        ArgumentNullException.ThrowIfNull(collection);

        Items.Clear();
        foreach (var i in collection) Items.Add(i);

        // Fire strict notification to reset UI binding efficiently
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

// FIX: Custom Permission Class for Android 12+ (API 31)
public class BluetoothConnectPermission : Permissions.BasePlatformPermission
{
#if ANDROID
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions
    {
        get
        {
            // CA1416 Fix: Guard API 31+ permissions with a runtime version check.
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                return
                [
                    (Android.Manifest.Permission.BluetoothConnect, true),
                    (Android.Manifest.Permission.BluetoothScan, true)
                ];
            }
            return [];
        }
    }
#endif
}