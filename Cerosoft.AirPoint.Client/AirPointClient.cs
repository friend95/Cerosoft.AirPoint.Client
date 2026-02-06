using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace Cerosoft.AirPoint.Client;

// 1. Implement Interface for Strategy Pattern
public class AirPointClient : IAirPointClient
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private volatile bool _isConnected;
    private volatile bool _isLoopRunning;

    // Mouse Coalescing (High-Freq Input Optimization)
    private float _pendingX;
    private float _pendingY;

    public event Action<string>? ConnectionLost;

    // 2. Update ConnectAsync signature to match interface (generic string address)
    public async Task<bool> ConnectAsync(string address)
    {
        // Robust Parsing Logic for "IP:PORT"
        if (string.IsNullOrWhiteSpace(address)) return false;

        string[] parts = address.Split(':');
        if (parts.Length != 2) return false;

        string ip = parts[0];
        if (!int.TryParse(parts[1], out int port)) return false;

        return await ConnectTcpAsync(ip, port);
    }

    // Renamed original method to helper
    private async Task<bool> ConnectTcpAsync(string ipAddress, int port)
    {
        try
        {
            Disconnect(suppressEvent: true);

            _client = new TcpClient
            {
                NoDelay = true, // Critical for low-latency input
                ReceiveBufferSize = 8192,
                SendBufferSize = 8192
            };

            // Modern: Use cancellation token for connection timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _client.ConnectAsync(ipAddress, port, cts.Token);

            _stream = _client.GetStream();
            _isConnected = true;
            _isLoopRunning = true;

            // Start the sender loop on background thread (Fire-and-Forget)
            _ = Task.Run(SenderLoop);
            return true;
        }
        catch
        {
            Disconnect(suppressEvent: true);
            return false;
        }
    }

    public void QueueMove(float x, float y)
    {
        // Thread-safe enough for float primitives in this context (Last-Write-Wins)
        _pendingX += x;
        _pendingY += y;
    }

    private async Task SenderLoop()
    {
        while (_isLoopRunning && _isConnected)
        {
            try
            {
                // Coalesce accumulated movement to prevent network saturation
                if (_pendingX != 0 || _pendingY != 0)
                {
                    float x = _pendingX;
                    float y = _pendingY;
                    _pendingX = 0;
                    _pendingY = 0;
                    await SendMoveInternal(x, y);
                }

                // 120Hz cap (approx 8ms) ensures smoothness without flooding
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
        // Modern Pattern Matching (IDE0041)
        if (_stream is null) return;

        // Zero-Allocation: Rent buffer from shared pool
        byte[] buffer = ArrayPool<byte>.Shared.Rent(9);
        try
        {
            buffer[0] = 1; // OpCode: Move
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(1), x);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(5), y);

            // Efficient async write using Memory<T>
            await _stream.WriteAsync(buffer.AsMemory(0, 9));
        }
        catch (Exception ex) { HandleError($"Move failed: {ex.Message}"); }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public async Task SendScroll(float amount)
    {
        if (_stream is null) return;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(5);
        try
        {
            buffer[0] = 4; // OpCode: Scroll
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(1), amount);
            await _stream.WriteAsync(buffer.AsMemory(0, 5));
        }
        catch (Exception ex) { HandleError($"Scroll failed: {ex.Message}"); }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public async Task SendZoom(float scaleDelta)
    {
        if (_stream is null) return;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(5);
        try
        {
            buffer[0] = 10; // OpCode: Zoom
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(1), scaleDelta);
            await _stream.WriteAsync(buffer.AsMemory(0, 5));
        }
        catch (Exception ex) { HandleError($"Zoom failed: {ex.Message}"); }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    // --- KEYBOARD / TEXT INPUT ---

    public async Task SendText(string text)
    {
        if (_stream is null || string.IsNullOrEmpty(text)) return;
        try
        {
            int byteCount = Encoding.UTF8.GetByteCount(text);
            int packetSize = 1 + 4 + byteCount; // OpCode + Length + Bytes

            byte[] buffer = ArrayPool<byte>.Shared.Rent(packetSize);
            try
            {
                Span<byte> span = buffer.AsSpan(0, packetSize);
                span[0] = 20; // OpCode: Text
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
        if (_stream is null) return;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(5);
        try
        {
            buffer[0] = 21; // OpCode: Key
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1), keyCode);
            await _stream.WriteAsync(buffer.AsMemory(0, 5));
        }
        catch (Exception ex) { HandleError($"Key send failed: {ex.Message}"); }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public async Task SendOpenUrl(string url)
    {
        if (_stream is null) return;
        try
        {
            int stringByteCount = Encoding.UTF8.GetByteCount(url);
            int packetSize = 1 + 4 + stringByteCount;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(packetSize);
            try
            {
                Span<byte> span = buffer.AsSpan(0, packetSize);
                span[0] = 6; // OpCode: OpenUrl
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
        if (_stream is null) return;
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
        if (_stream is null) return;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(2);
        try
        {
            buffer[0] = 5; // OpCode: Shortcut
            buffer[1] = shortcutId;
            await _stream.WriteAsync(buffer.AsMemory(0, 2));
        }
        catch (Exception ex) { HandleError($"Shortcut failed: {ex.Message}"); }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    // Windows+Tab behavior (Shortcut ID 6)
    public async Task SendTaskView() => await SendShortcut(6);

    public async Task SendShutdown() => await SendSimpleCommand(7, "Shutdown");
    public async Task SendRestart() => await SendSimpleCommand(11, "Restart");
    public async Task SendLock() => await SendSimpleCommand(12, "Lock");

    private async Task SendSimpleCommand(byte commandId, string commandName)
    {
        if (_stream is null) return;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1);
        try
        {
            buffer[0] = commandId;
            await _stream.WriteAsync(buffer.AsMemory(0, 1));
        }
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
        _client = null;
        _stream = null;

        if (wasConnected && !suppressEvent && !string.IsNullOrEmpty(reason))
        {
            ConnectionLost?.Invoke(reason);
        }
    }

    // Add this accessor for Interface Compliance
    public bool IsConnected() => _isConnected;
}