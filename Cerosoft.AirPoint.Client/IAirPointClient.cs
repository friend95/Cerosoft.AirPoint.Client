using System;
using System.Threading.Tasks;

namespace Cerosoft.AirPoint.Client
{
    public interface IAirPointClient
    {
        event Action<string>? ConnectionLost;

        // Connectivity
        Task<bool> ConnectAsync(string address); // Address can be IP or MAC
        void Disconnect(string? reason = null, bool suppressEvent = false);
        bool IsConnected();

        // Core Input
        void QueueMove(float x, float y);
        Task SendClick(byte type);
        Task SendScroll(float amount);
        Task SendZoom(float scaleDelta);

        // Gestures & Keyboard
        Task SendLeftDown();
        Task SendLeftUp();
        Task SendText(string text);
        Task SendKey(int keyCode);
        Task SendShortcut(byte shortcutId);
        Task SendTaskView();

        // System
        Task SendOpenUrl(string url);
        Task SendLock();
        Task SendRestart();
        Task SendShutdown();
    }
}