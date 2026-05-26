using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Haukcode.HighResolutionTimer.Implementations
{
    internal sealed class LinuxTimerImplementation : ITimerImplementation
    {
        private readonly int fd;
        private readonly int cancellationFd;
        private TimeSpan period;
        
        public LinuxTimerImplementation()
        {
            var timer = timerfd_create(CLOCK_MONOTONIC, TFD_CLOEXEC);
            
            if (timer == -1)
            {
                throw new Exception($"Unable to create timer, errno = {Marshal.GetLastWin32Error()}");
            }

            var cancellation = eventfd(0, EFD_CLOEXEC);

            if (cancellation == -1)
            {
                close(timer);
                throw new Exception($"Unable to create cancellation event, errno = {Marshal.GetLastWin32Error()}");
            }
            
            this.fd = timer;
            this.cancellationFd = cancellation;
        }

        public TimeSpan Period
        {
            get => this.period;
            set
            {
                const long nanosecondsPerTick = 100;
                
                var seconds = new IntPtr(value.Ticks / TimeSpan.TicksPerSecond);
                var nanoseconds = new IntPtr(value.Ticks % TimeSpan.TicksPerSecond * nanosecondsPerTick);
                
                var itval = new itimerspec
                {
                    it_interval = new timespec { tv_sec = seconds, tv_nsec = nanoseconds },
                    it_value = new timespec { tv_sec = seconds, tv_nsec = nanoseconds }
                };
                
                var result = timerfd_settime(this.fd, 0, ref itval, IntPtr.Zero);

                if (result != 0)
                {
                    throw new Exception($"Error from timerfd_settime = {Marshal.GetLastWin32Error()}");
                }
                
                this.period = value;
            }
        }
        
        public void Dispose()
        {
            close(this.cancellationFd);
            close(this.fd);
        }
        
        public void Wait(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (cancellationToken.Register(state => SignalCancellation((int)state), this.cancellationFd))
            {
                var fds = new[]
                {
                    new pollfd { fd = this.fd, events = POLLIN },
                    new pollfd { fd = this.cancellationFd, events = POLLIN }
                };

                while (true)
                {
                    var result = poll(fds, new UIntPtr((uint)fds.Length), -1);

                    if (result < 0)
                    {
                        var errno = Marshal.GetLastWin32Error();

                        if (errno == EINTR)
                        {
                            continue;
                        }

                        throw new Exception($"Error from poll = {errno}");
                    }

                    if ((fds[1].revents & POLLIN) != 0)
                    {
                        ReadExpirationCount(this.cancellationFd, "eventfd");
                        throw new OperationCanceledException(cancellationToken);
                    }

                    if ((fds[0].revents & POLLIN) != 0)
                    {
                        ReadExpirationCount(this.fd, "timerfd");
                        return;
                    }

                    if ((fds[0].revents | fds[1].revents) != 0)
                    {
                        throw new Exception($"Unexpected poll result, timerfd revents = {fds[0].revents}, eventfd revents = {fds[1].revents}");
                    }
                }
            }
        }

        private static ulong ReadExpirationCount(int fileDescriptor, string name)
        {
            while (true)
            {
                ulong value;
                var result = read(fileDescriptor, out value, new UIntPtr(sizeof(ulong))).ToInt64();

                if (result == sizeof(ulong))
                {
                    return value;
                }

                if (result < 0)
                {
                    var errno = Marshal.GetLastWin32Error();

                    if (errno == EINTR)
                    {
                        continue;
                    }

                    throw new Exception($"Error from {name} read = {errno}");
                }

                throw new Exception($"Unexpected {name} read result = {result}");
            }
        }

        private static void SignalCancellation(int fileDescriptor)
        {
            while (true)
            {
                ulong value = 1;
                var result = write(fileDescriptor, ref value, new UIntPtr(sizeof(ulong))).ToInt64();

                if (result == sizeof(ulong))
                {
                    return;
                }

                if (result < 0 && Marshal.GetLastWin32Error() == EINTR)
                {
                    continue;
                }

                return;
            }
        }

        private const int CLOCK_REALTIME = 0;
        private const int CLOCK_MONOTONIC = 1;
        private const int CLOCK_PROCESS_CPUTIME_ID = 2;
        private const int CLOCK_THREAD_CPUTIME_ID = 3;
        private const int CLOCK_MONOTONIC_RAW = 4;
        private const int CLOCK_REALTIME_COARSE = 5;
        private const int CLOCK_MONOTONIC_COARSE = 6;
        private const int CLOCK_BOOTTIME = 7;
        private const int CLOCK_REALTIME_ALARM = 8;
        private const int CLOCK_BOOTTIME_ALARM = 9;
        private const int TFD_CLOEXEC = 0x80000;
        private const int EFD_CLOEXEC = 0x80000;
        private const int EINTR = 4;
        private const short POLLIN = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct pollfd
        {
            public int fd;
            public short events;
            public short revents;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct timespec
        {
            public IntPtr tv_sec;   /* seconds */
            public IntPtr tv_nsec;  /* nanoseconds */
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct itimerspec
        {
            public timespec it_interval;    /* timer period */
            public timespec it_value;       /* timer expiration */
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int timerfd_create(
            int clockId,
            int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int timerfd_settime(
            int fd,
            int flags,
            ref itimerspec newValue,
            IntPtr oldValue);

        [DllImport("libc", SetLastError = true)]
        private static extern IntPtr read(
            int fd,
            out ulong buf,
            UIntPtr count);

        [DllImport("libc", SetLastError = true)]
        private static extern IntPtr write(
            int fd,
            ref ulong buf,
            UIntPtr count);

        [DllImport("libc", SetLastError = true)]
        private static extern int eventfd(
            uint initval,
            int flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int poll(
            [In, Out] pollfd[] fds,
            UIntPtr nfds,
            int timeout);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);
    }
}
