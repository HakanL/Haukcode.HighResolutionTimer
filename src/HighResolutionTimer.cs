using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Haukcode.HighResolutionTimer.Implementations;

namespace Haukcode.HighResolutionTimer
{
    /// <summary>
    /// High performance (precision) timer
    /// </summary>
    public class HighResolutionTimer : ITimer, IDisposable
    {
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly ManualResetEventSlim startedSignal = new ManualResetEventSlim(false);
        private readonly AutoResetEvent triggerEvent = new AutoResetEvent(false);
        private readonly ITimerImplementation implementation;
        private readonly Thread thread;
        private TimeSpan? period;
        private int disposed;

        public HighResolutionTimer()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                this.implementation = new Win32TimerImplementation();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                this.implementation = new LinuxTimerImplementation();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                this.implementation = new MacOSTimerImplementation();
            }
            else
            {
                throw new PlatformNotSupportedException();
            }

            // High priority reduces wake-up latency while waiting on the native
            // timer, improving blocking precision. Background mode keeps this
            // helper thread from holding the process alive on application exit.
            this.thread = new Thread(RunTimerLoop)
            {
                Name = nameof(HighResolutionTimer),
                Priority = ThreadPriority.Highest,
                IsBackground = true
            };
            this.thread.Start();
        }

        private void RunTimerLoop()
        {
            const int stopped = 0;
            const int started = 1;

            try
            {
                switch (stopped)
                {
                    case stopped:
                    {
                        this.startedSignal.Wait(this.cts.Token);
                        goto case started;
                    }
                    case started:
                    {
                        // ReSharper disable once PossibleInvalidOperationException
                        // `stopped` state validates that period is set
                        this.implementation.Period = this.period.Value;
                        while (this.startedSignal.IsSet)
                        {
                            // Apply a period change requested via SetPeriod() while the timer is
                            // running. The blocking Wait() below is not interrupted, so the new
                            // period takes effect from the iteration after the next tick (as the
                            // SetPeriod docs promise). Without this the running period was fixed at
                            // the value set before the first Start() — a Stop()/SetPeriod()/Start()
                            // re-arm silently did nothing because the loop never re-read the field.
                            var requestedPeriod = this.period.Value;
                            if (requestedPeriod != this.implementation.Period)
                                this.implementation.Period = requestedPeriod;

                            this.implementation.Wait(cts.Token);
                            triggerEvent.Set();
                        }
                        goto case stopped;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.CompareExchange(ref this.disposed, 1, 0) == 0)
            {
                if (disposing)
                {
                    this.cts.Cancel();

                    // Release a WaitForTrigger consumer blocked without a token; the
                    // post-wait ThrowIfDisposed will surface ObjectDisposedException.
                    this.triggerEvent.Set();

                    // Wait for the scheduler thread to exit during explicit disposal only.
                    // Avoid deadlocking by joining the current thread.
                    if (Thread.CurrentThread != this.thread)
                    {
                        this.thread.Join();
                    }

                    this.implementation.Dispose();

                    this.cts.Dispose();
                    this.triggerEvent.Dispose();
                    this.startedSignal.Dispose();
                }
            }
        }

        protected void ThrowIfDisposed()
        {
            if (Volatile.Read(ref this.disposed) == 1)
            {
                throw new ObjectDisposedException(nameof(HighResolutionTimer));
            }
        }

        /// <summary>
        /// Set the frequency of the timer in milliseconds. For example 25 ms would generate a 40 Hz timer (1000/25=40)
        /// </summary>
        /// <param name="periodMS">Period in MS</param>
        public void SetPeriod(double periodMS)
        {
            SetPeriod(TimeSpan.FromMilliseconds(periodMS));
        }

        /// <summary>
        /// Set the frequency of the timer. While running, the new period takes effect
        /// from the iteration after the next tick — the in-flight wait is not interrupted.
        /// </summary>
        /// <param name="period">Period</param>
        public void SetPeriod(TimeSpan period)
        {
            ThrowIfDisposed();

            var minPeriod = TimeSpan.FromMilliseconds(1);
            var maxPeriod = TimeSpan.FromMinutes(15);

            if (minPeriod > period || period > maxPeriod)
            {
                throw new ArgumentOutOfRangeException(nameof(period), "Period cannot be greater than 15 minutes or less than 1 ms.");
            }

            this.period = period;
        }

        public void Start()
        {
            ThrowIfDisposed();

            if (!this.period.HasValue)
            {
                throw new InvalidOperationException("Set a period before starting the timer.");
            }
            
            this.startedSignal.Set();
        }

        public void Stop()
        {
            ThrowIfDisposed();
            this.startedSignal.Reset();
        }

        /// <summary>
        /// Wait for the next trigger
        /// </summary>
        public void WaitForTrigger()
        {
            WaitForTrigger(CancellationToken.None);
        }

        /// <summary>
        /// Wait for the next trigger, observing a cancellation token.
        /// </summary>
        public void WaitForTrigger(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (!cancellationToken.CanBeCanceled)
            {
                this.triggerEvent.WaitOne();
            }
            else
            {
                var index = WaitHandle.WaitAny(new WaitHandle[]
                {
                    this.triggerEvent,
                    cancellationToken.WaitHandle
                });

                if (index == 1)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }

        /// <summary>
        /// Dispose all resources
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~HighResolutionTimer() => Dispose(disposing: false);
    }
}
