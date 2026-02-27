using System;
using System.Diagnostics;
using Haukcode.HighResolutionTimer;

namespace HighResolutionTimer.Tests
{
    public class HighResolutionTimerTests
    {
        [Fact]
        public void Constructor_CreatesTimerSuccessfully()
        {
            using var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            Assert.NotNull(timer);
        }

        [Fact]
        public void SetPeriod_ValidPeriod_DoesNotThrow()
        {
            using var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            timer.SetPeriod(10);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void SetPeriod_PeriodLessThanOne_ThrowsArgumentOutOfRangeException(double periodMs)
        {
            using var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            Assert.Throws<ArgumentOutOfRangeException>(() => timer.SetPeriod(periodMs));
        }

        [Fact]
        public void SetPeriod_PeriodGreaterThanMaximum_ThrowsArgumentOutOfRangeException()
        {
            using var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            double tooLarge = TimeSpan.FromMinutes(15).TotalMilliseconds + 1;
            Assert.Throws<ArgumentOutOfRangeException>(() => timer.SetPeriod(tooLarge));
        }

        [Fact]
        public void StartAndStop_DoesNotThrow()
        {
            using var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            timer.SetPeriod(50);
            timer.Start();
            timer.Stop();
        }

        [Fact]
        public void WaitForTrigger_FiresWithinReasonableTime()
        {
            using var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            timer.SetPeriod(50);
            timer.Start();

            var sw = Stopwatch.StartNew();
            timer.WaitForTrigger();
            sw.Stop();

            timer.Stop();

            // Allow up to 5 seconds for the trigger to fire to accommodate slow CI environments
            Assert.True(sw.ElapsedMilliseconds < 5000, $"WaitForTrigger took {sw.ElapsedMilliseconds}ms, expected less than 5000ms");
        }

        [Fact]
        public void WaitForTrigger_MultipleTriggers_FiresApproximatelyAtPeriod()
        {
            const int periodMs = 50;
            const int triggerCount = 5;

            using var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            timer.SetPeriod(periodMs);
            timer.Start();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < triggerCount; i++)
            {
                timer.WaitForTrigger();
            }
            sw.Stop();

            timer.Stop();

            long elapsed = sw.ElapsedMilliseconds;
            // Minimum: half the expected time. Maximum: 60 seconds to accommodate slow CI environments
            long expectedMin = (long)(periodMs * triggerCount * 0.5);
            long expectedMax = 60_000;

            Assert.True(elapsed >= expectedMin && elapsed <= expectedMax,
                $"Elapsed time {elapsed}ms was outside expected range [{expectedMin}ms, {expectedMax}ms]");
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            timer.Dispose();
            // Second Dispose should not throw
            timer.Dispose();
        }
    }
}
