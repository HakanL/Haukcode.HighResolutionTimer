using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Haukcode.HighResolutionTimer
{
    /// <summary>
    /// A timer based on the Windows Waitable Timer API with sub-millisecond precision.
    /// Uses SetWaitableTimer with a 100ns-resolution due time and manual re-arm in a
    /// scheduler thread, matching the timerfd approach used on Linux.
    /// </summary>
    internal class WindowsTimer : ITimer, IDisposable
    {
        private bool disposed = false;
        private volatile bool schedulerHandleClosed = false;
        private double periodMs;
        private bool isRunning;
        private readonly IntPtr timerHandle;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly ManualResetEvent triggerEvent = new ManualResetEvent(false);
        private readonly Thread thread;

        public WindowsTimer()
        {
            // false = auto-reset: timer resets to non-signaled automatically after WaitForSingleObject returns
            this.timerHandle = WindowsNativeMethods.CreateWaitableTimer(
                IntPtr.Zero, false, IntPtr.Zero);

            if (this.timerHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error);
            }
            
            // High priority reduces wake-up latency while waiting on the native
            // timer, improving blocking precision. Background mode keeps this
            // helper thread from holding the process alive on application exit.
            this.thread = new Thread(Scheduler)
            {
                Name = nameof(HighResolutionTimer),
                Priority = ThreadPriority.Highest,
                IsBackground = true
            };
            this.thread.Start();
        }

        ~WindowsTimer()
        {
            Dispose(false);
        }

        public void SetPeriod(double periodMS)
        {
            this.periodMs = periodMS;
        }

        private void Scheduler()
        {
            while (!this.cts.IsCancellationRequested)
            {
                double period = this.periodMs;
                if (period <= 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                // Convert ms to 100ns intervals; negative value = relative time from now.
                // This gives sub-millisecond precision (e.g. 16.67ms = -166700 units).
                long dueTime = -(long)(period * 10_000);

                if (!WindowsNativeMethods.SetWaitableTimer(
                    this.timerHandle, ref dueTime, 0,
                    IntPtr.Zero, IntPtr.Zero, false))
                {
                    break;
                }

                uint result = WindowsNativeMethods.WaitForSingleObject(
                    this.timerHandle, WindowsNativeMethods.INFINITE);

                if (result != 0)
                    break;

                if (this.isRunning && !this.cts.IsCancellationRequested)
                    this.triggerEvent.Set();
            }

            this.schedulerHandleClosed = true;
            WindowsNativeMethods.CloseHandle(this.timerHandle);
            this.cts.Dispose();
            this.triggerEvent.Dispose();
        }

        public void Start()
        {
            CheckDisposed();
            this.isRunning = true;
        }

        public void Stop()
        {
            CheckDisposed();
            this.isRunning = false;
        }

        public void WaitForTrigger()
        {
            this.triggerEvent.WaitOne();
            this.triggerEvent.Reset();
        }

        private void CheckDisposed()
        {
            if (this.disposed)
                throw new ObjectDisposedException("WindowsTimer");
        }

        private void Dispose(bool disposing)
        {
            if (this.disposed)
                return;

            this.disposed = true;
            this.isRunning = false;
            this.cts.Cancel();

            // Signal the timer immediately to unblock WaitForSingleObject in the scheduler thread.
            // Guard against the handle already being closed by the scheduler thread.
            if (!this.schedulerHandleClosed)
            {
                long dueTime = -1; // 100ns = fires almost immediately
                WindowsNativeMethods.SetWaitableTimer(
                    this.timerHandle, ref dueTime, 0,
                    IntPtr.Zero, IntPtr.Zero, false);
            }
            
            if (disposing)
            {
                GC.SuppressFinalize(this);
            }

            // Wait for the scheduler thread to exit.
            this.thread.Join();
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }

    internal static class WindowsNativeMethods
    {
        internal const uint INFINITE = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateWaitableTimer(
            IntPtr lpTimerAttributes,
            bool bManualReset,
            IntPtr lpTimerName);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetWaitableTimer(
            IntPtr hTimer,
            ref long lpDueTime,
            int lPeriod,
            IntPtr pfnCompletionRoutine,
            IntPtr lpArgToCompletionRoutine,
            bool fResume);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr hObject);
    }
}
