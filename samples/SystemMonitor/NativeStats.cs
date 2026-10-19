using System;
using System.Runtime.InteropServices;

namespace SwissTechKnife.Samples.SystemMonitor
{
    /// <summary>Cheap system counters (no PerformanceCounter warm-up cost).</summary>
    internal sealed class NativeStats
    {
        private ulong _lastIdle;
        private ulong _lastTotal;

        /// <summary>CPU usage since the previous call, 0..1.</summary>
        public double SampleCpu()
        {
            if (!GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user))
            {
                return 0;
            }
            ulong idleTicks = ToUInt64(idle);
            ulong total = ToUInt64(kernel) + ToUInt64(user); // kernel time includes idle time
            ulong idleDelta = idleTicks - _lastIdle;
            ulong totalDelta = total - _lastTotal;
            bool first = _lastTotal == 0;
            _lastIdle = idleTicks;
            _lastTotal = total;
            if (first || totalDelta == 0)
            {
                return 0;
            }
            return Math.Max(0, Math.Min(1, 1.0 - (double)idleDelta / totalDelta));
        }

        public static (ulong Total, ulong Available) Memory()
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            return GlobalMemoryStatusEx(ref status) ? (status.ullTotalPhys, status.ullAvailPhys) : (0UL, 0UL);
        }

        public static TimeSpan Uptime() => TimeSpan.FromMilliseconds(GetTickCount64());

        private static ulong ToUInt64(FILETIME time) => ((ulong)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public int dwLowDateTime;
            public int dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();
    }
}
