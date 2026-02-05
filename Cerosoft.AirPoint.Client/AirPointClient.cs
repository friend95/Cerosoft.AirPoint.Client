using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace Cerosoft.AirPoint.Client
{
    public class AirPointClient
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private volatile bool _isConnected;

        private float _pendingX = 0;
        private float _pendingY = 0;
        private bool _isLoopRunning = false;

        public event Action<string>? ConnectionLost;

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            try
            {
                Disconnect();

                _client = new TcpClient
                {
                    NoDelay = true,
                    ReceiveBufferSize = 8192,
                    SendBufferSize = 8192
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _client.ConnectAsync(ipAddress, port, cts.Token);

                _stream = _client.GetStream();
                _isConnected = true;
                _isLoopRunning = true;

                _ = Task.Run(SenderLoop);
                return true;
            }
            catch (Exception)
            {
                Disconnect(suppressEvent: true);
                return false;
            }
        }

        public void QueueMove(float x, float y)
        {
            _pendingX += x;
            _pendingY += y;
        }

        private async Task SenderLoop()
        {
            while (_isLoopRunning && _isConnected)
            {
                try
                {
                    if (_pendingX != 0 || _pendingY != 0)
                    {
                        float x = _pendingX;
                        float y = _pendingY;
                        _pendingX = 0; _pendingY = 0;
                        await SendMoveInternal(x, y);
                    }
                    await Task.Delay(8);
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
            if (_stream == null) return;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(9);
            try
            {
                buffer[0] = 1;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(1), x);
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(5), y);
                await _stream.WriteAsync(buffer.AsMemory(0, 9));
            }
            catch (Exception ex) { HandleError($"Move failed: {ex.Message}"); }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public async Task SendScroll(float amount)
        {
            if (_stream == null) return;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(5);
            try
            {
                buffer[0] = 4;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(1), amount);
                await _stream.WriteAsync(buffer.AsMemory(0, 5));
            }
            catch (Exception ex) { HandleError($"Scroll failed: {ex.Message}"); }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public async Task SendZoom(float scaleDelta)
        {
            if (_stream == null) return;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(5);
            try
            {
                buffer[0] = 10;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(1), scaleDelta);
                await _stream.WriteAsync(buffer.AsMemory(0, 5));
            }
            catch (Exception ex) { HandleError($"Zoom failed: {ex.Message}"); }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        // --- KEYBOARD / TEXT INPUT ---

        public async Task SendText(string text)
        {
            if (_stream == null || string.IsNullOrEmpty(text)) return;
            try
            {
                int byteCount = Encoding.UTF8.GetByteCount(text);
                int packetSize = 1 + 4 + byteCount;

                byte[] buffer = ArrayPool<byte>.Shared.Rent(packetSize);
                try
                {
                    Span<byte> span = buffer.AsSpan(0, packetSize);
                    span[0] = 20;
                    BinaryPrimitives.WriteInt32LittleEndian(span[1..], byteCount);
                    Encoding.UTF8.GetBytes(text, span[5..]);

                    await _stream.WriteAsync(buffer.AsMemory(0, packetSize));
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
            catch (Exception ex) { HandleError($"Text send failed: {ex.Message}"); }
        }

        public async Task SendKey(int keyCode)
        {
            if (_stream == null) return;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(5);
            try
            {
                buffer[0] = 21;
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1), keyCode);
                await _stream.WriteAsync(buffer.AsMemory(0, 5));
            }
            catch (Exception ex) { HandleError($"Key send failed: {ex.Message}"); }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public async Task SendOpenUrl(string url)
        {
            if (_stream == null) return;
            try
            {
                int stringByteCount = Encoding.UTF8.GetByteCount(url);
                int packetSize = 1 + 4 + stringByteCount;
                byte[] buffer = ArrayPool<byte>.Shared.Rent(packetSize);
                try
                {
                    Span<byte> span = buffer.AsSpan(0, packetSize);
                    span[0] = 6;
                    BinaryPrimitives.WriteInt32LittleEndian(span[1..], stringByteCount);
                    Encoding.UTF8.GetBytes(url, span[5..]);
                    await _stream.WriteAsync(buffer.AsMemory(0, packetSize));
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
            catch (Exception ex) { HandleError($"Open URL failed: {ex.Message}"); }
        }

        public async Task SendClick(byte type)
        {
            if (_stream == null) return;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(1);
            try
            {
                buffer[0] = type;
                await _stream.WriteAsync(buffer.AsMemory(0, 1));
            }
            catch (Exception ex) { HandleError($"Click failed: {ex.Message}"); }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public async Task SendLeftDown() => await SendSimpleCommand(8, "LeftDown");
        public async Task SendLeftUp() => await SendSimpleCommand(9, "LeftUp");

        public async Task SendShortcut(byte shortcutId)
        {
            if (_stream == null) return;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(2);
            try { buffer[0] = 5; buffer[1] = shortcutId; await _stream.WriteAsync(buffer.AsMemory(0, 2)); }
            catch (Exception ex) { HandleError($"Shortcut failed: {ex.Message}"); }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        // NEW: Explicit Task View Command (Windows+Tab behavior)
        // Uses Shortcut ID 6 which corresponds to Task View on the server side
        public async Task SendTaskView() => await SendShortcut(6);

        public async Task SendShutdown() => await SendSimpleCommand(7, "Shutdown");
        public async Task SendRestart() => await SendSimpleCommand(11, "Restart");
        public async Task SendLock() => await SendSimpleCommand(12, "Lock");

        private async Task SendSimpleCommand(byte commandId, string commandName)
        {
            if (_stream == null) return;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(1);
            try { buffer[0] = commandId; await _stream.WriteAsync(buffer.AsMemory(0, 1)); }
            catch (Exception ex) { HandleError($"{commandName} failed: {ex.Message}"); }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private void HandleError(string _)
        {
            if (_isConnected)
            {
                Disconnect(reason: "Connection to server interrupted.");
            }
        }

        public void Disconnect(string? reason = null, bool suppressEvent = false)
        {
            bool wasConnected = _isConnected;
            _isLoopRunning = false;
            _isConnected = false;
            try { _client?.Close(); _client?.Dispose(); } catch { }
            _client = null; _stream = null;

            if (wasConnected && !suppressEvent && !string.IsNullOrEmpty(reason))
            {
                ConnectionLost?.Invoke(reason);
            }
        }
    }
}