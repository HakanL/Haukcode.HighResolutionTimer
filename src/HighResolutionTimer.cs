﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Haukcode.HighResolutionTimer
{
    /// <summary>
    /// High performance (precision) timer
    /// </summary>
    public class HighResolutionTimer : ITimer, IDisposable
    {
        private readonly ITimer timer;

        public HighResolutionTimer()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                this.timer = new MultimediaTimer();
            else
            {
                if (IntPtr.Size == 8)
                    this.timer = new LinuxTimer64();
                else
                    this.timer = new LinuxTimer();
            }
        }

        /// <summary>
        /// Dispose all resources
        /// </summary>
        public void Dispose()
        {
            this.timer.Dispose();
        }

        /// <summary>
        /// Set the frequency of the timer in milliseconds. For example 25 ms would generate a 40 Hz timer (1000/25=40)
        /// </summary>
        /// <param name="periodMS">Period in MS</param>
        public void SetPeriod(int periodMS)
        {
            if (periodMS < 1 || periodMS > TimeSpan.FromMinutes(15).TotalMilliseconds)
                throw new ArgumentOutOfRangeException(nameof(periodMS), "Period cannot be greater than 15 minutes and has to be greater than 0 mS");

            this.timer.SetPeriod(periodMS);
        }

        public void Start()
        {
            this.timer.Start();
        }

        public void Stop()
        {
            this.timer.Stop();
        }

        /// <summary>
        /// Wait for the next trigger
        /// </summary>
        public void WaitForTrigger()
        {
            this.timer.WaitForTrigger();
        }
    }
}
