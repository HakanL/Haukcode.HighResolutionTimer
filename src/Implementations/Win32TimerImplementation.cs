using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Haukcode.HighResolutionTimer.Implementations
{
    internal sealed class Win32TimerImplementation : ITimerImplementation
    {
        private readonly IntPtr handle;
        private TimeSpan period;
        
        public Win32TimerImplementation()
        {
            var timer = CreateWaitableTimer(IntPtr.Zero, false, IntPtr.Zero);

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
                // Convert ms to 100ns intervals; negative value = relative time from now.
                // 1 TimeSpan tick = 100 nanoseconds, which matches the FILETIME unit.
                // This gives sub-millisecond precision (e.g. 16.67ms = -166700 units).
                var dueTime = -value.Ticks;

                if (!SetWaitableTimer(this.handle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    ThrowWin32Exception();
                }
                
                this.period = value;
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

            this.Period = this.period;
        }

        private static void ThrowWin32Exception()
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateWaitableTimer(
            IntPtr lpTimerAttributes,
            bool bManualReset,
            IntPtr lpTimerName);

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