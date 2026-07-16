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
            const uint infinite = 0xFFFFFFFF;
            const uint waitError = 0xFFFFFFFF;
            const uint waitCancelled = 1;
            
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!cancellationToken.CanBeCanceled)
            {
                switch (WaitForSingleObject(this.handle, infinite))
                {
                    case waitError:
                        ThrowWin32Exception();
                        return; // never returns
                }
            }
            else
            {
                var waitHandles = new[]
                {
                    this.handle,
                    cancellationToken.WaitHandle.SafeWaitHandle.DangerousGetHandle()
                };

                switch (WaitForMultipleObjects((uint)waitHandles.Length, waitHandles, false, infinite))
                {
                    case waitCancelled:
                        throw new OperationCanceledException(cancellationToken);
                    
                    case waitError:
                        ThrowWin32Exception();
                        return; // never returns
                }
            }

            // Advance the absolute deadline by exactly one period. If we have fallen
            // behind by a full period or more (system suspend, debugger break, clock
            // step), re-anchor at now to avoid a burst of immediate catch-up ticks.
            this.nextDueFileTime += this.period.Ticks;

            GetSystemTimePreciseAsFileTime(out long now);
            if (this.nextDueFileTime <= now)
            {
                this.nextDueFileTime = now + this.period.Ticks;
            }

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