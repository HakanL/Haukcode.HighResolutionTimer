using System;

namespace Haukcode.HighResolutionTimer
{
    public interface ITimer : IDisposable
    {
        void SetPeriod(double periodMS);

        void WaitForTrigger();

        void Start();

        void Stop();
    }
}
