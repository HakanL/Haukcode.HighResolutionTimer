using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Haukcode.HighResolutionTimer.Implementations
{
    internal sealed class MacOSTimerImplementation : ITimerImplementation
    {
        private readonly int kqueueFd;
        private TimeSpan period;

        public MacOSTimerImplementation()
        {
            var fd = kqueue();

            if (fd == -1)
            {
                throw new Exception($"Unable to create kqueue, errno = {Marshal.GetLastWin32Error()}");
            }

            this.kqueueFd = fd;

            // Pre-register a user-signaled event so cancellation can break a blocked kevent().
            // Without this, the native wait cannot observe CancellationToken changes.
            var registerCancellation = new[]
            {
                new KEvent
                {
                    ident = new UIntPtr(CancellationIdent),
                    filter = EVFILT_USER,
                    flags = EV_ADD | EV_CLEAR,
                    fflags = 0,
                    data = IntPtr.Zero,
                    udata = IntPtr.Zero
                }
            };

            if (kevent(this.kqueueFd, registerCancellation, 1, null, 0, IntPtr.Zero) == -1)
            {
                var errno = Marshal.GetLastWin32Error();
                close(this.kqueueFd);
                throw new Exception($"Unable to register cancellation event, errno = {errno}");
            }
        }

        public TimeSpan Period
        {
            get => this.period;
            set
            {
                // 1 TimeSpan tick = 100 nanoseconds; NOTE_NSECONDS keeps sub-microsecond precision.
                const long nanosecondsPerTick = 100;

                var change = new[]
                {
                    new KEvent
                    {
                        ident = new UIntPtr(TimerIdent),
                        filter = EVFILT_TIMER,
                        flags = EV_ADD | EV_ENABLE,
                        fflags = NOTE_NSECONDS,
                        data = new IntPtr(value.Ticks * nanosecondsPerTick),
                        udata = IntPtr.Zero
                    }
                };

                if (kevent(this.kqueueFd, change, 1, null, 0, IntPtr.Zero) == -1)
                {
                    throw new Exception($"Error from kevent = {Marshal.GetLastWin32Error()}");
                }

                this.period = value;
            }
        }

        public void Dispose()
        {
            close(this.kqueueFd);
        }

        public void Wait(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (cancellationToken.Register(state => SignalCancellation((int)state), this.kqueueFd))
            {
                var events = new KEvent[2];

                while (true)
                {
                    var result = kevent(this.kqueueFd, null, 0, events, events.Length, IntPtr.Zero);

                    if (result < 0)
                    {
                        var errno = Marshal.GetLastWin32Error();

                        if (errno == EINTR)
                        {
                            continue;
                        }

                        throw new Exception($"Error from kevent = {errno}");
                    }

                    for (var i = 0; i < result; i++)
                    {
                        if (events[i].filter == EVFILT_USER && events[i].ident.ToUInt64() == CancellationIdent)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }
                    }

                    for (var i = 0; i < result; i++)
                    {
                        if (events[i].filter == EVFILT_TIMER && events[i].ident.ToUInt64() == TimerIdent)
                        {
                            return;
                        }
                    }
                }
            }
        }

        private static void SignalCancellation(int kqueueFd)
        {
            var change = new[]
            {
                new KEvent
                {
                    ident = new UIntPtr(CancellationIdent),
                    filter = EVFILT_USER,
                    flags = 0,
                    fflags = NOTE_TRIGGER,
                    data = IntPtr.Zero,
                    udata = IntPtr.Zero
                }
            };

            kevent(kqueueFd, change, 1, null, 0, IntPtr.Zero);
        }

        private const ulong TimerIdent = 1;
        private const ulong CancellationIdent = 2;

        private const short EVFILT_TIMER = -7;
        private const short EVFILT_USER = -10;

        private const ushort EV_ADD = 0x0001;
        private const ushort EV_ENABLE = 0x0004;
        private const ushort EV_CLEAR = 0x0020;

        private const uint NOTE_NSECONDS = 0x00000004;
        private const uint NOTE_TRIGGER = 0x01000000;

        private const int EINTR = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEvent
        {
            public UIntPtr ident;
            public short filter;
            public ushort flags;
            public uint fflags;
            public IntPtr data;
            public IntPtr udata;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int kqueue();

        [DllImport("libc", SetLastError = true)]
        private static extern int kevent(
            int kq,
            [In] KEvent[] changelist,
            int nchanges,
            [In, Out] KEvent[] eventlist,
            int nevents,
            IntPtr timeout);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);
    }
}
