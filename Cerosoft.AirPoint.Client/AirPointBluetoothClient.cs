using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if ANDROID
using Android.Bluetooth;
using Android.Content;
using Java.Util;
#endif

namespace Cerosoft.AirPoint.Client
{
    public class AirPointBluetoothClient : IAirPointClient
    {
        // Standard SPP UUID (Serial Port Profile)
        private static readonly Guid SppUuid = Guid.Parse("00001101-0000-1000-8000-00805F9B34FB");

#if ANDROID
        private BluetoothSocket? _socket;
#endif
        private volatile bool _isConnected;
        private volatile bool _isLoopRunning;

        // HIGH-PERFORMANCE INPUT BUFFERING
        // FIX IDE0330: Use 'System.Threading.Lock' instead of 'object' for .NET 9+ optimization.
        // The C# 'lock' statement automatically uses Lock.EnterScope() with this type.
        private readonly System.Threading.Lock _inputLock = new();

        private float _pendingX = 0;
        private float _pendingY = 0;

        // SIGNALING: Replaces Task.Delay. 
        // This semaphore wakes the SenderLoop immediately when input arrives.
        private readonly SemaphoreSlim _inputSignal = new(0);

        public event Action<string>? ConnectionLost;

        public async Task<bool> ConnectAsync(string macAddress)
        {
#if ANDROID
            try
            {
                Disconnect(suppressEvent: true);

                // FIX CA1422: Modern BluetoothManager for API 31+
                var context = Android.App.Application.Context;
                var manager = context.GetSystemService(Context.BluetoothService) as BluetoothManager;
                var adapter = manager?.Adapter;

                if (adapter == null || !adapter.IsEnabled) return false;

                var device = adapter.GetRemoteDevice(macAddress.ToUpper());
                if (device == null) return false;

                // Create insecure RFCOMM for broader compatibility and faster pairing
                _socket = device.CreateRfcommSocketToServiceRecord(UUID.FromString(SppUuid.ToString()));
                if (_socket == null) return false;

                await _socket.ConnectAsync();

                _isConnected = true;
                _isLoopRunning = true;

                // Start the sender loop on a LongRunning thread to avoid ThreadPool starvation
                _ = Task.Factory.StartNew(SenderLoop, TaskCreationOptions.LongRunning);

                return true;
            }
            catch (Exception)
            {
                Disconnect(suppressEvent: true);
                return false;
            }
#else
            return await Task.FromResult(false);
#endif
        }

        // --- STATIC AUTO-PAIRING LOGIC ---
        public static string? FindPairedDevice()
        {
#if ANDROID
            var context = Android.App.Application.Context;
            var manager = context.GetSystemService(Context.BluetoothService) as BluetoothManager;
            var adapter = manager?.Adapter;

            if (adapter?.BondedDevices == null) return null;

            foreach (var device in adapter.BondedDevices)
            {
                var uuids = device.GetUuids();
                if (uuids != null)
                {
                    foreach (var uuid in uuids)
                    {
                        if (uuid.ToString().Equals(SppUuid.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            return device.Address;
                        }
                    }
                }
            }
#endif
            return null;
        }

        // --- HOT PATH: INPUT QUEUE ---
        public void QueueMove(float x, float y)
        {
            // Thread-safe accumulation using the new System.Threading.Lock
            lock (_inputLock)
            {
                _pendingX += x;
                _pendingY += y;
            }

            // Wake up the sender loop immediately if it's sleeping
            if (_inputSignal.CurrentCount == 0)
            {
                try { _inputSignal.Release(); } catch { }
            }
        }

        private async Task SenderLoop()
        {
            while (_isLoopRunning && _isConnected)
            {
                try
                {
                    // Zero-CPU Wait: Sleeps until 'QueueMove' signals data is ready.
                    // This eliminates the 10ms latency floor.
                    await _inputSignal.WaitAsync();

                    float xToSend = 0;
                    float yToSend = 0;

                    // Atomic Capture & Reset
                    lock (_inputLock)
                    {
                        xToSend = _pendingX;
                        yToSend = _pendingY;
                        _pendingX = 0;
                        _pendingY = 0;
                    }

                    // Only send if there is actual movement
                    if (xToSend != 0 || yToSend != 0)
                    {
                        await SendMoveInternal(xToSend, yToSend);
                    }
                }
                catch (Exception ex)
                {
                    HandleError($"Transmission loop error: {ex.Message}");
                    break;
                }
            }
        }

        private async Task SendMoveInternal(float x, float y)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(9);
            try
            {
                buffer[0] = 1; // Move OpCode
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(1), x);
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(5), y);
                await WriteAsync(buffer, 9);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private async Task WriteAsync(byte[] buffer, int length)
        {
#if ANDROID
            if (_socket?.OutputStream == null) return;
            try
            {
                // Direct write to the output stream. 
                // The OS Bluetooth stack handles the buffering.
                await _socket.OutputStream.WriteAsync(buffer.AsMemory(0, length));
            }
            catch (Exception ex)
            {
                HandleError(ex.Message);
            }
#else
            await Task.CompletedTask;
#endif
        }

        // --- COMMAND IMPLEMENTATIONS ---

        public async Task SendClick(byte type) => await SendSimplePayload(type);

        public async Task SendScroll(float amount)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(5);
            try
            {
                buffer[0] = 4;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(1), amount);
                await WriteAsync(buffer, 5);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public async Task SendZoom(float scaleDelta)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(5);
            try
            {
                buffer[0] = 10;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(1), scaleDelta);
                await WriteAsync(buffer, 5);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public async Task SendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int byteCount = Encoding.UTF8.GetByteCount(text);
            int packetSize = 1 + 4 + byteCount;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(packetSize);
            try
            {
                buffer[0] = 20;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1), byteCount);
                Encoding.UTF8.GetBytes(text, buffer.AsSpan(5));
                await WriteAsync(buffer, packetSize);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public async Task SendKey(int keyCode)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(5);
            try
            {
                buffer[0] = 21;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1), keyCode);
                await WriteAsync(buffer, 5);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public async Task SendOpenUrl(string url)
        {
            int byteCount = Encoding.UTF8.GetByteCount(url);
            int packetSize = 1 + 4 + byteCount;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(packetSize);
            try
            {
                buffer[0] = 6;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1), byteCount);
                Encoding.UTF8.GetBytes(url, buffer.AsSpan(5));
                await WriteAsync(buffer, packetSize);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public async Task SendShortcut(byte shortcutId)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(2);
            try { buffer[0] = 5; buffer[1] = shortcutId; await WriteAsync(buffer, 2); }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        // Shorthands
        public async Task SendLeftDown() => await SendSimpleCommand(8);
        public async Task SendLeftUp() => await SendSimpleCommand(9);
        public async Task SendTaskView() => await SendShortcut(6);
        public async Task SendShutdown() => await SendSimpleCommand(7);
        public async Task SendRestart() => await SendSimpleCommand(11);
        public async Task SendLock() => await SendSimpleCommand(12);

        private async Task SendSimpleCommand(byte commandId) => await SendSimplePayload(commandId);

        private async Task SendSimplePayload(byte b)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(1);
            try { buffer[0] = b; await WriteAsync(buffer, 1); }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private void HandleError(string reason)
        {
            if (_isConnected)
            {
                Disconnect(reason);
            }
        }

        public void Disconnect(string? reason = null, bool suppressEvent = false)
        {
            bool wasConnected = _isConnected;
            _isConnected = false;
            _isLoopRunning = false;

#if ANDROID
            try { _socket?.Close(); _socket?.Dispose(); } catch { }
            _socket = null;
#endif

            if (wasConnected && !suppressEvent && !string.IsNullOrEmpty(reason))
            {
                ConnectionLost?.Invoke(reason);
            }
        }

        public bool IsConnected() => _isConnected;
    }
}