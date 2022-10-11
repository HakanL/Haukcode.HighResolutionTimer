using System;
using System.Runtime.InteropServices;

namespace Haukcode.HighResolutionTimer
{
// Disable these StyleCop rules for this file, as we are using native names here.
#pragma warning disable SA1300 // Element should begin with upper-case letter

    internal partial class Interop64
    {
        private const string LibcLibrary = "libc";

        public enum ClockIds : int
        {
            CLOCK_REALTIME = 0,
            CLOCK_MONOTONIC = 1,
            CLOCK_PROCESS_CPUTIME_ID = 2,
            CLOCK_THREAD_CPUTIME_ID = 3,
            CLOCK_MONOTONIC_RAW = 4,
            CLOCK_REALTIME_COARSE = 5,
            CLOCK_MONOTONIC_COARSE = 6,
            CLOCK_BOOTTIME = 7,
            CLOCK_REALTIME_ALARM = 8,
            CLOCK_BOOTTIME_ALARM = 9
        }

        [StructLayout(LayoutKind.Explicit)]
        public class timespec64
        {
            [FieldOffset(0)]
            public ulong tv_sec;                 /* seconds */
            [FieldOffset(8)]
            public ulong tv_nsec;                /* nanoseconds */
        };

        [StructLayout(LayoutKind.Explicit)]
        public class itimerspec64
        {
            [FieldOffset(0)]
            public timespec64 it_interval;    /* timer period */

            [FieldOffset(16)]
            public timespec64 it_value;       /* timer expiration */
        };

        [DllImport(LibcLibrary, SetLastError = true)]
        internal static extern int timerfd_create(ClockIds clockId, int flags);

        [DllImport(LibcLibrary, SetLastError = true)]
        internal static extern int timerfd_settime(int fd, int flags, itimerspec64 new_value, itimerspec64 old_value);

        [DllImport(LibcLibrary, SetLastError = true)]
        internal static extern long read(int fd, IntPtr buf, ulong count);

        [DllImport(LibcLibrary)]
        internal static extern int close(int fd);
    }
}
