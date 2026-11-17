using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace HighResolutionTimer.Tests
{
    public class HighResolutionTimerTests
    {
        private readonly ITestOutputHelper _output;

        public HighResolutionTimerTests(ITestOutputHelper output)
        {
            _output = output;
        }

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

        [Fact]
        public void Accuracy_25msPeriod_CountsExpectedTriggersIn500ms()
        {
            const int periodMs = 25;
            const int windowMs = 500;
            int count = 0;
            int expectedCount = windowMs / periodMs; // 20

            using var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            timer.SetPeriod(periodMs);
            timer.Start();

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < windowMs)
            {
                timer.WaitForTrigger();
                count++;
            }

            timer.Stop();
            sw.Stop();

            double actualPeriodMs = sw.Elapsed.TotalMilliseconds / count;
            _output.WriteLine($"Period: {periodMs}ms, Window: {windowMs}ms");
            _output.WriteLine($"Expected triggers: ~{expectedCount}, Actual triggers: {count}");
            _output.WriteLine($"Actual average period: {actualPeriodMs:F2}ms");

            // Accept 25%–400% of the expected count to accommodate heavily loaded CI runners
            int minCount = Math.Max(1, expectedCount / 4);
            int maxCount = expectedCount * 4;
            Assert.True(count >= minCount && count <= maxCount,
                $"Trigger count {count} outside tolerance [{minCount}, {maxCount}] for {periodMs}ms period over {windowMs}ms window");
        }

        [Fact]
        public void SetPeriod_WhileRunning_ChangesTriggerRate()
        {
            using var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            timer.SetPeriod(50);
            timer.Start();

            // Discard the first trigger (may include arm latency), then measure the slow rate.
            timer.WaitForTrigger();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++)
                timer.WaitForTrigger();
            double slowPeriodMs = sw.Elapsed.TotalMilliseconds / 5;

            // Change the period while running to a much faster rate.
            timer.SetPeriod(10);

            // Let the change take effect (up to one old period) and discard the transition ticks.
            timer.WaitForTrigger();
            timer.WaitForTrigger();

            sw.Restart();
            for (int i = 0; i < 10; i++)
                timer.WaitForTrigger();
            double fastPeriodMs = sw.Elapsed.TotalMilliseconds / 10;

            timer.Stop();

            _output.WriteLine($"slow ~{slowPeriodMs:F1}ms, fast ~{fastPeriodMs:F1}ms");

            // Regression guard for the re-arm bug: while running, the period was fixed at the value
            // set before the first Start(), so SetPeriod() had no effect. 50ms -> 10ms is a 5x
            // change; the tolerance stays loose for heavily loaded CI runners.
            Assert.True(fastPeriodMs < slowPeriodMs * 0.6,
                $"SetPeriod while running had no effect: slow ~{slowPeriodMs:F1}ms vs fast ~{fastPeriodMs:F1}ms");
        }

        [Fact]
        public void SetPeriod_BeforeStart_FirstTriggerNotImmediate()
        {
            const int periodMs = 100;

            using var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            timer.SetPeriod(periodMs);

            // Long enough that a spuriously-queued tick from SetPeriod would have surfaced.
            Thread.Sleep(periodMs * 3);

            var sw = Stopwatch.StartNew();
            timer.Start();
            timer.WaitForTrigger();
            sw.Stop();

            timer.Stop();

            Assert.True(sw.ElapsedMilliseconds >= periodMs / 2,
                $"First trigger fired after {sw.ElapsedMilliseconds}ms — expected ~{periodMs}ms (SetPeriod before Start must not leak an early tick)");
        }

        [Fact]
        public async Task WaitForTrigger_CancellationToken_ThrowsOnCancel()
        {
            using var timer = new Haukcode.HighResolutionTimer.HighResolutionTimer();
            timer.SetPeriod(60_000);
            timer.Start();

            using var cts = new CancellationTokenSource();
            var task = Task.Run(() => timer.WaitForTrigger(cts.Token));

            await Task.Delay(100);
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => task);

            timer.Stop();
        }

        [Fact]
        public void Win32_ClockStepBack_KeepsTicking()
        {
            if (!OperatingSystem.IsWindows())
                return;

            // The Windows implementation arms absolute wall-clock deadlines. A backward clock
            // step leaves the armed deadline that far in the future; without a bounded wait the
            // timer went silent for the whole step (seen in the field as a hung output thread).
            using var implementation = new Haukcode.HighResolutionTimer.Implementations.Win32TimerImplementation();
            implementation.Period = TimeSpan.FromMilliseconds(10);
            implementation.Wait(CancellationToken.None);

            implementation.SimulateClockStepBack(TimeSpan.FromMinutes(1));

            var sw = Stopwatch.StartNew();
            implementation.Wait(CancellationToken.None);
            long stalledWaitMs = sw.ElapsedMilliseconds;

            // Re-anchored at now, so the following ticks come at the normal period again
            sw.Restart();
            for (int i = 0; i < 10; i++)
                implementation.Wait(CancellationToken.None);
            long tenTicksMs = sw.ElapsedMilliseconds;

            _output.WriteLine($"Wait after step: {stalledWaitMs} ms, next 10 ticks: {tenTicksMs} ms");

            Assert.True(stalledWaitMs < 1_000, $"Wait after a 1 minute clock step took {stalledWaitMs} ms");
            Assert.True(tenTicksMs < 500, $"10 ticks at 10 ms took {tenTicksMs} ms after re-anchoring");
        }

        [Theory]
        [InlineData(1, 52)]
        [InlineData(16.67, 84)]
        [InlineData(25, 100)]
        [InlineData(15 * 60 * 1000, 1_800_050)]
        public void Win32_WaitTimeout_IsTwoPeriodsPlusMargin(double periodMs, uint expectedMs)
        {
            uint timeoutMs = Haukcode.HighResolutionTimer.Implementations.Win32TimerImplementation.GetWaitTimeoutMS(TimeSpan.FromMilliseconds(periodMs));

            Assert.Equal(expectedMs, timeoutMs);
        }
    }
}
