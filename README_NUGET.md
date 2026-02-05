# Haukcode.HighResolutionTimer

A cross-platform, high-resolution timer for .NET Standard 2.0+ with precise timing for Windows and Linux.

## Installation

```bash
dotnet add package Haukcode.HighResolutionTimer
```

## Quick Start

```csharp
using Haukcode.HighResolutionTimer;

// Create a 10ms period timer (100 Hz)
using (var timer = new HighResolutionTimer())
{
    timer.SetPeriod(10);  // Set period in milliseconds
    timer.Start();

    for (int i = 0; i < 100; i++)
    {
        timer.WaitForTrigger();  // Wait for next tick
        // Your periodic code here
    }

    timer.Stop();
}
```

## Key Features

- **High Precision**: ~1ms on Windows, microsecond on Linux
- **Cross-Platform**: Automatic Windows/Linux detection
- **Simple API**: Easy to use with minimal setup
- **Period Range**: 0 to 15 minutes (900,000 ms)

## Common Use Cases

- Real-time data processing
- Game loops (e.g., 60 Hz: `timer.SetPeriod(16.67)`)
- Multimedia applications
- Control systems
- Any scenario requiring precise periodic execution

## Platform Support

- **Windows**: Multimedia Timer API (~1ms precision)
- **Linux**: timerfd API (microsecond precision)
- **Compatibility**: .NET Core 2.0+, .NET 5+, .NET Framework 4.6.1+

## Full Documentation

For detailed documentation, examples, and API reference, visit:
https://github.com/HakanL/Haukcode.HighResolutionTimer

## License

MIT License - See [LICENSE](https://github.com/HakanL/Haukcode.HighResolutionTimer/blob/master/LICENSE)
