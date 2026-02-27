using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Haukcode.HighResolutionTimer
{
    /// <summary>
    /// A timer based on the Windows Timer Queue API with 1ms precision.
    /// </summary>
    internal class WindowsTimer : ITimer, IDisposable
    {
        private bool disposed = false;
        private IntPtr timerHandle = IntPtr.Zero;
        private uint periodMs;
        private readonly ManualResetEvent triggerEvent = new ManualResetEvent(false);

        // Hold the timer callback to prevent garbage collection.
        private readonly TimerQueueCallback callback;

        public WindowsTimer()
        {
            this.callback = new TimerQueueCallback(TimerCallbackMethod);
        }

        ~WindowsTimer()
        {
            Dispose(false);
        }

        private bool IsRunning => this.timerHandle != IntPtr.Zero;

        public void SetPeriod(double periodMS)
        {
            this.periodMs = (uint)periodMS;
        }

        public void Start()
        {
            CheckDisposed();

            if (IsRunning)
                throw new InvalidOperationException("Timer is already running");

            bool success = WindowsNativeMethods.CreateTimerQueueTimer(
                out this.timerHandle,
                IntPtr.Zero,    // use default timer queue
                this.callback,
                IntPtr.Zero,    // no parameter
                this.periodMs,  // due time
                this.periodMs,  // period
                0);             // WT_EXECUTEDEFAULT

            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                this.timerHandle = IntPtr.Zero;
                throw new Win32Exception(error);
            }
        }

        public void Stop()
        {
            CheckDisposed();

            if (!IsRunning)
                throw new InvalidOperationException("Timer has not been started");

            StopInternal();
        }

        private void StopInternal()
        {
            if (this.timerHandle != IntPtr.Zero)
            {
                if (!WindowsNativeMethods.DeleteTimerQueueTimer(IntPtr.Zero, this.timerHandle, IntPtr.Zero))
                {
                    // ERROR_IO_PENDING (997) is the expected result when CompletionEvent is NULL
                    // and callbacks are still running; the timer will be deleted once they complete.
                    // Any other error code indicates an unexpected failure.
                    _ = Marshal.GetLastWin32Error();
                }
                this.timerHandle = IntPtr.Zero;
                this.triggerEvent.Set();
            }
        }

        private void TimerCallbackMethod(IntPtr parameter, bool timerOrWaitFired)
        {
            this.triggerEvent.Set();
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
            if (IsRunning)
            {
                StopInternal();
            }

            if (disposing)
            {
                this.triggerEvent.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public void WaitForTrigger()
        {
            this.triggerEvent.WaitOne();
            this.triggerEvent.Reset();
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void TimerQueueCallback(IntPtr parameter, bool timerOrWaitFired);

    internal static class WindowsNativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CreateTimerQueueTimer(
            out IntPtr phNewTimer,
            IntPtr TimerQueue,
            TimerQueueCallback Callback,
            IntPtr Parameter,
            uint DueTime,
            uint Period,
            uint Flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool DeleteTimerQueueTimer(
            IntPtr TimerQueue,
            IntPtr Timer,
            IntPtr CompletionEvent);
    }
}
