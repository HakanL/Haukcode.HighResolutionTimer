using System;
using System.Threading;

namespace Haukcode.HighResolutionTimer.Implementations
{
    internal interface ITimerImplementation : IDisposable
    {
        TimeSpan Period { get; set; }

        void Wait(CancellationToken cancellationToken);
    }
}