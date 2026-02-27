using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Haukcode.HighResolutionTimer
{
    internal class MacOSTimer : ITimer, IDisposable
    {
        private readonly int kqueueFd;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly ManualResetEvent triggerEvent = new ManualResetEvent(false);
        private bool isRunning;

        private const short EVFILT_TIMER = -7;
        private const ushort EV_ADD = 0x0001;
        private const ushort EV_ENABLE = 0x0004;
        private const uint NOTE_USECONDS = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEvent
        {
            public ulong ident;
            public short filter;
            public ushort flags;
            public uint fflags;
            public long data;
            public IntPtr udata;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int kqueue();

        [DllImport("libc", SetLastError = true)]
        private static extern int kevent(int kq, ref KEvent changelist, int nchanges, IntPtr eventlist, int nevents, IntPtr timeout);

        [DllImport("libc", SetLastError = true, EntryPoint = "kevent")]
        private static extern int kevent_wait(int kq, IntPtr changelist, int nchanges, ref KEvent eventlist, int nevents, IntPtr timeout);

        [DllImport("libc")]
        private static extern int close(int fd);

        public MacOSTimer()
        {
            this.kqueueFd = kqueue();

            if (this.kqueueFd == -1)
                throw new Exception($"Unable to create kqueue, errno = {Marshal.GetLastWin32Error()}");

            ThreadPool.QueueUserWorkItem(Scheduler);
        }

        public void WaitForTrigger()
        {
            this.triggerEvent.WaitOne();
            this.triggerEvent.Reset();
        }

        public void SetPeriod(double periodMS)
        {
            var kev = new KEvent
            {
                ident = 1,
                filter = EVFILT_TIMER,
                flags = EV_ADD | EV_ENABLE,
                fflags = NOTE_USECONDS,
                data = (long)(periodMS * 1000),
                udata = IntPtr.Zero
            };

            int ret = kevent(this.kqueueFd, ref kev, 1, IntPtr.Zero, 0, IntPtr.Zero);
            if (ret == -1)
                throw new Exception($"Error from kevent = {Marshal.GetLastWin32Error()}");
        }

        private void Scheduler(object state)
        {
            while (!this.cts.IsCancellationRequested)
            {
                var result = new KEvent();
                int ret = kevent_wait(this.kqueueFd, IntPtr.Zero, 0, ref result, 1, IntPtr.Zero);

                if (ret > 0 && this.isRunning)
                    this.triggerEvent.Set();
            }

            close(this.kqueueFd);
            this.cts.Dispose();
            this.triggerEvent.Dispose();
        }

        public void Dispose()
        {
            this.cts.Cancel();

            // Release trigger
            this.triggerEvent.Set();
        }

        public void Start()
        {
            this.isRunning = true;
        }

        public void Stop()
        {
            this.isRunning = false;
        }
    }
}
