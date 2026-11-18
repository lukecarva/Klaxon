using System.Runtime.InteropServices;
using Klaxon.Core.Abstractions;

namespace Klaxon.App.Services;

/// <summary>Idle time from the last keyboard/mouse input, via GetLastInputInfo.</summary>
public sealed class Win32IdleDetector : IIdleDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    /// <summary>Time since the last input; assumes present if the call fails.</summary>
    public TimeSpan IdleFor
    {
        get
        {
            var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };

            if (!GetLastInputInfo(ref info))
            {
                return TimeSpan.Zero;
            }

            // uint subtraction stays correct across the 32-bit tick wraparound.
            var agora = (uint)Environment.TickCount;
            var decorrido = agora - info.dwTime;

            return TimeSpan.FromMilliseconds(decorrido);
        }
    }
}
