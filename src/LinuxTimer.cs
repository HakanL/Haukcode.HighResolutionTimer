using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Haukcode.HighResolutionTimer
{
    internal class LinuxTimer : ITimer, IDisposable
    {
        private readonly int fileDescriptor;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly ManualResetEvent triggerEvent = new ManualResetEvent(false);
        private readonly Thread thread;
        private bool isRunning;
        private bool disposed;

        public LinuxTimer()
        {
            this.fileDescriptor = Interop.timerfd_create(Interop.ClockIds.CLOCK_MONOTONIC, 0);

            if (this.fileDescriptor == -1)
                throw new Exception($"Unable to create timer, errno = {Marshal.GetLastWin32Error()}");

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

        public void WaitForTrigger()
        {
            this.triggerEvent.WaitOne();
            this.triggerEvent.Reset();
        }

        public void SetPeriod(double periodMS)
        {
            SetFrequency((uint)(periodMS * 1_000));
        }

        private void Scheduler()
        {
            while (!this.cts.IsCancellationRequested)
            {
                Wait();

                if (this.isRunning)
                    this.triggerEvent.Set();
            }

            Interop.close(this.fileDescriptor);
        }

        private void SetFrequency(uint period)
        {
            uint sec = period / 1000000;
            uint ns = (period - (sec * 1000000)) * 1000;
            var itval = new Interop.itimerspec
            {
                it_interval = new Interop.timespec
                {
                    tv_sec = sec,
                    tv_nsec = ns
                },
                it_value = new Interop.timespec
                {

                    tv_sec = sec,
                    tv_nsec = ns
                }
            };

            int ret = Interop.timerfd_settime(this.fileDescriptor, 0, itval, null);
            if (ret != 0)
                throw new Exception($"Error from timerfd_settime = {Marshal.GetLastWin32Error()}");
        }

        private long Wait()
        {
            // Wait for the next timer event. If we have missed any the number is written to "missed"
            byte[] buf = new byte[16];
            var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
            IntPtr pointer = handle.AddrOfPinnedObject();
            int ret = Interop.read(this.fileDescriptor, pointer, buf.Length);
            // ret = bytes read
            long missed = Marshal.ReadInt64(pointer);
            handle.Free();

            if (ret < 0)
                throw new Exception($"Error in read = {Marshal.GetLastWin32Error()}");

            return missed;
        }

        public void Dispose()
        {
            if (this.disposed)
                return;

            this.disposed = true;
            this.cts.Cancel();

            // Arm timerfd to fire immediately, unblocking the scheduler's blocking read.
            // CancellationToken alone does not interrupt Interop.read on the timerfd.
            var itval = new Interop.itimerspec
            {
                it_interval = new Interop.timespec { tv_sec = 0, tv_nsec = 0 },
                it_value = new Interop.timespec { tv_sec = 0, tv_nsec = 1 }
            };
            Interop.timerfd_settime(this.fileDescriptor, 0, itval, null);

            // Release trigger
            this.triggerEvent.Set();

            // Wait for the scheduler thread to exit during explicit disposal only.
            // Avoid deadlocking by joining the current thread.
            if (Thread.CurrentThread != this.thread)
            {
                this.thread.Join();
            }

            this.cts.Dispose();
            this.triggerEvent.Dispose();
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
