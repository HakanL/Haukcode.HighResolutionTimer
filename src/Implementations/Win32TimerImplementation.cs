using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Haukcode.HighResolutionTimer.Implementations
{
    internal sealed class Win32TimerImplementation : ITimerImplementation
    {
        // High-resolution waitable timers (Windows 10 1803+) are not quantized to the
        // ~15.6ms system clock tick, so short periods like 1ms are honored accurately
        // without raising the global timer resolution via timeBeginPeriod.
        private const uint CreateWaitableTimerHighResolution = 0x00000002;
        private const uint TimerAllAccess = 0x1F0003;

        private readonly IntPtr handle;
        private TimeSpan period;
        private long nextDueFileTime; // absolute FILETIME (100ns units since 1601-01-01 UTC)

        public Win32TimerImplementation()
        {
            var timer = CreateWaitableTimerEx(IntPtr.Zero, IntPtr.Zero, CreateWaitableTimerHighResolution, TimerAllAccess);

            if (timer == IntPtr.Zero)
            {
                ThrowWin32Exception();
            }

            this.handle = timer;
        }
        
        public void Dispose()
        {
            CloseHandle(this.handle);
        }

        public TimeSpan Period
        {
            get => this.period;
            set
            {
                // (Re)anchor the tick schedule at now + period, using ABSOLUTE due times.
                // Wait() advances the deadline by exactly one period per tick, so wake-up
                // and re-arm latency never accumulate into the effective period. (The
                // previous relative one-shot re-arm added that latency on every cycle,
                // measured as ~25.3ms for a 25ms period — 1.3% slow.)
                // 1 TimeSpan tick = 100 nanoseconds, which matches the FILETIME unit,
                // giving sub-millisecond precision (e.g. 16.67ms = 166700 units).
                GetSystemTimePreciseAsFileTime(out long now);
                this.nextDueFileTime = now + value.Ticks;
                this.period = value;

                Arm(this.nextDueFileTime);
            }
        }

        public void Wait(CancellationToken cancellationToken)
        {
            const uint waitError = 0xFFFFFFFF;
            const uint waitTimeout = 0x00000102;
            const uint waitCancelled = 1;

            cancellationToken.ThrowIfCancellationRequested();

            // The due times are absolute wall-clock times, so a backward step of the system
            // clock (a time sync correction, typically right after a fresh install or a resume)
            // pushes the armed deadline into the future by the size of the step, and the timer
            // would go silent for that long. A healthy wait never takes much more than one
            // period, so give up after two and re-anchor; the late tick is still delivered.
            uint timeoutMS = GetWaitTimeoutMS(this.period);
            uint result;

            if (!cancellationToken.CanBeCanceled)
            {
                result = WaitForSingleObject(this.handle, timeoutMS);
            }
            else
            {
                var waitHandles = new[]
                {
                    this.handle,
                    cancellationToken.WaitHandle.SafeWaitHandle.DangerousGetHandle()
                };

                result = WaitForMultipleObjects((uint)waitHandles.Length, waitHandles, false, timeoutMS);

                if (result == waitCancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            if (result == waitError)
            {
                ThrowWin32Exception();
            }

            GetSystemTimePreciseAsFileTime(out long now);

            if (result == waitTimeout)
            {
                this.nextDueFileTime = now + this.period.Ticks;
            }
            else
            {
                // Advance the absolute deadline by exactly one period. If we have fallen
                // behind by a full period or more (system suspend, debugger break, forward
                // clock step), re-anchor at now to avoid a burst of immediate catch-up ticks.
                // If the deadline is more than a period ahead, the clock stepped back while
                // this tick was being handled; re-anchor rather than wait out the step.
                this.nextDueFileTime += this.period.Ticks;

                if (this.nextDueFileTime <= now || this.nextDueFileTime - now > this.period.Ticks)
                {
                    this.nextDueFileTime = now + this.period.Ticks;
                }
            }

            Arm(this.nextDueFileTime);
        }

        internal static uint GetWaitTimeoutMS(TimeSpan period)
        {
            // Two periods plus a margin for scheduling latency on a loaded system. Periods are
            // limited to 15 minutes, so this always fits and never reaches INFINITE.
            return (uint)Math.Ceiling(period.TotalMilliseconds * 2) + 50;
        }

        /// <summary>
        /// Test hook: moves the armed deadline later by <paramref name="step"/>, which is what
        /// the kernel sees when the system clock steps back by that amount.
        /// </summary>
        internal void SimulateClockStepBack(TimeSpan step)
        {
            this.nextDueFileTime += step.Ticks;

            Arm(this.nextDueFileTime);
        }

        private void Arm(long dueFileTime)
        {
            // Positive due time = absolute FILETIME (UTC wall clock)
            if (!SetWaitableTimer(this.handle, ref dueFileTime, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                ThrowWin32Exception();
            }
        }

        private static void ThrowWin32Exception()
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        
        [DllImport("kernel32.dll")]
        private static extern void GetSystemTimePreciseAsFileTime(out long lpSystemTimeAsFileTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateWaitableTimerEx(
            IntPtr lpTimerAttributes,
            IntPtr lpTimerName,
            uint dwFlags,
            uint dwDesiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimer(
            IntPtr hTimer,
            ref long lpDueTime,
            int lPeriod,
            IntPtr pfnCompletionRoutine,
            IntPtr lpArgToCompletionRoutine,
            bool fResume);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(
            IntPtr hHandle,
            uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForMultipleObjects(
            uint nCount,
            IntPtr[] lpHandles,
            bool bWaitAll,
            uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}