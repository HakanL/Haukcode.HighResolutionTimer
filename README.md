# Haukcode.HighResolutionTimer [![NuGet Version](http://img.shields.io/nuget/v/Haukcode.HighResolutionTimer.svg?style=flat)](https://www.nuget.org/packages/Haukcode.HighResolutionTimer/)

A high-resolution, cross-platform timer for .NET Standard 2.0+ applications that provides precise timing capabilities for Windows, Linux, and macOS.

## Overview

`Haukcode.HighResolutionTimer` is a lightweight library that abstracts platform-specific high-resolution timer implementations to provide consistent, precise timing across different operating systems:

- **Windows**: Uses Multimedia Timer API (`timeSetEvent`) with ~1ms precision
- **Linux**: Uses `timerfd` API with microsecond precision
- **macOS**: Uses `kqueue`/`kevent` API with microsecond precision
- **Cross-platform**: Automatic platform detection and selection

This library is ideal for applications requiring accurate periodic execution, such as real-time data processing, game loops, multimedia applications, or any scenario where `System.Threading.Timer` doesn't provide sufficient precision.

## Features

- ✅ Cross-platform support (Windows, Linux, and macOS)
- ✅ High precision timing (1ms on Windows, microsecond on Linux and macOS)
- ✅ Simple, intuitive API
- ✅ Automatic platform detection
- ✅ Support for both 32-bit and 64-bit Linux
- ✅ .NET Standard 2.0 compatible
- ✅ Configurable timer period
- ✅ Thread-safe implementation
- ✅ Disposable pattern for proper resource cleanup

## Installation

Install via NuGet Package Manager:

```bash
dotnet add package Haukcode.HighResolutionTimer
```

Or via Package Manager Console:

```powershell
Install-Package Haukcode.HighResolutionTimer
```

Or add directly to your `.csproj` file:

```xml
<PackageReference Include="Haukcode.HighResolutionTimer" Version="1.2.0" />
```

## Quick Start

Here's a simple example to get you started:

```csharp
using Haukcode.HighResolutionTimer;
using System;

class Program
{
    static void Main()
    {
        // Create a timer with 10ms period (100 Hz)
        using (var timer = new HighResolutionTimer())
        {
            timer.SetPeriod(10); // 10 milliseconds
            timer.Start();

            // Run for 1 second
            for (int i = 0; i < 100; i++)
            {
                timer.WaitForTrigger();
                Console.WriteLine($"Tick {i} at {DateTime.Now:HH:mm:ss.fff}");
            }

            timer.Stop();
        }
    }
}
```

## Usage Examples

### Basic Timer with Custom Period

```csharp
using (var timer = new HighResolutionTimer())
{
    // Set period to 25ms (40 Hz)
    timer.SetPeriod(25);
    timer.Start();

    for (int i = 0; i < 10; i++)
    {
        timer.WaitForTrigger();
        // Your periodic code here
        Console.WriteLine("Tick!");
    }

    timer.Stop();
}
```

### Real-Time Data Processing Loop

```csharp
using (var timer = new HighResolutionTimer())
{
    timer.SetPeriod(1); // 1ms period for high-frequency processing
    timer.Start();

    bool keepRunning = true;
    while (keepRunning)
    {
        timer.WaitForTrigger();
        
        // Process real-time data
        ProcessSensorData();
        
        // Check exit condition
        if (Console.KeyAvailable)
        {
            keepRunning = false;
        }
    }

    timer.Stop();
}
```

### Game Loop Example

```csharp
using (var timer = new HighResolutionTimer())
{
    const double targetFPS = 60.0;
    double framePeriod = 1000.0 / targetFPS; // ~16.67ms
    
    timer.SetPeriod(framePeriod);
    timer.Start();

    bool running = true;
    while (running)
    {
        timer.WaitForTrigger();
        
        // Update game state
        UpdateGameLogic();
        
        // Render frame
        RenderFrame();
        
        // Check for exit
        running = !ShouldExit();
    }

    timer.Stop();
}
```

### Async/Await Pattern

```csharp
public async Task RunTimerAsync(CancellationToken cancellationToken)
{
    using (var timer = new HighResolutionTimer())
    {
        timer.SetPeriod(100); // 100ms
        timer.Start();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Run(() => timer.WaitForTrigger(), cancellationToken);
                
                // Your periodic work
                await DoWorkAsync();
            }
        }
        finally
        {
            timer.Stop();
        }
    }
}
```

## API Reference

### HighResolutionTimer Class

The main class implementing `ITimer` and `IDisposable` interfaces.

#### Constructor

```csharp
public HighResolutionTimer()
```

Creates a new high-resolution timer instance. Automatically selects the appropriate platform-specific implementation.

#### Methods

##### SetPeriod

```csharp
public void SetPeriod(double periodMS)
```

Sets the timer period in milliseconds.

- **Parameters:**
  - `periodMS` (double): Period in milliseconds. Must be greater than 0 and less than 15 minutes (900,000 ms).
- **Throws:**
  - `ArgumentOutOfRangeException`: If period is out of valid range.

**Example:**
```csharp
timer.SetPeriod(10);    // 10ms period (100 Hz)
timer.SetPeriod(1);     // 1ms period (1000 Hz)
timer.SetPeriod(16.67); // ~60 Hz (common for games)
```

##### Start

```csharp
public void Start()
```

Starts the timer. Must be called before `WaitForTrigger()`.

- **Throws:**
  - `InvalidOperationException`: If timer is already running.

##### Stop

```csharp
public void Stop()
```

Stops the timer. Can be started again with `Start()`.

- **Throws:**
  - `InvalidOperationException`: If timer is not running.

##### WaitForTrigger

```csharp
public void WaitForTrigger()
```

Blocks the current thread until the next timer event occurs. This method should be called in a loop to process periodic events.

**Important:** Must call `Start()` before using this method.

##### Dispose

```csharp
public void Dispose()
```

Releases all resources used by the timer. Automatically stops the timer if running.

## Platform-Specific Details

### Windows Implementation

- Uses Windows Multimedia Timer API (`winmm.dll`)
- Typical precision: ~1ms
- Resolution can be adjusted but defaults to 5ms
- Works on all Windows versions that support .NET Standard 2.0

### Linux Implementation

- Uses `timerfd_create` and `timerfd_settime` system calls
- Precision: Microsecond level
- Separate implementations for 32-bit and 64-bit systems
- Requires Linux kernel 2.6.25 or later (timerfd support)

### macOS Implementation

- Uses `kqueue`/`kevent` system calls with `NOTE_USECONDS` flag
- Precision: Microsecond level
- Works on all macOS versions that support .NET Standard 2.0

## Performance Considerations

### Precision vs. CPU Usage

- Lower period values (higher frequencies) increase CPU usage
- On Windows, the multimedia timer affects system-wide timer resolution
- Consider your application's actual timing requirements

### Best Practices

1. **Use appropriate periods**: Don't use 1ms if 10ms will suffice
2. **Dispose properly**: Always use `using` statement or call `Dispose()` when done
3. **Avoid blocking operations**: Keep work in `WaitForTrigger()` loop minimal
4. **Consider threading**: For heavy work, consider offloading to separate threads
5. **Test on target platform**: Timing precision can vary by hardware and OS

### Example with Performance Measurement

```csharp
using System.Diagnostics;

using (var timer = new HighResolutionTimer())
{
    timer.SetPeriod(10); // 10ms target
    timer.Start();

    var stopwatch = Stopwatch.StartNew();
    long lastElapsed = 0;

    for (int i = 0; i < 100; i++)
    {
        timer.WaitForTrigger();
        
        long currentElapsed = stopwatch.ElapsedMilliseconds;
        long actualPeriod = currentElapsed - lastElapsed;
        lastElapsed = currentElapsed;
        
        Console.WriteLine($"Tick {i}: Actual period = {actualPeriod}ms");
    }

    timer.Stop();
}
```

## Requirements

- .NET Standard 2.0 or higher
- Compatible with:
  - .NET Core 2.0+
  - .NET 5+
  - .NET Framework 4.6.1+
  - Mono 5.4+
  - Xamarin
- Windows or Linux or macOS operating system

## Limitations

- Period must be between 0 and 15 minutes (900,000 ms)
- Timer precision depends on OS and hardware capabilities
- High-frequency timers (< 5ms) may impact overall system performance

## Troubleshooting

### Common Issues

**Timer not triggering on Linux:**
- Ensure your kernel version supports timerfd (Linux 2.6.25+)
- Check permissions if running in a restricted environment

**Inconsistent timing on Windows:**
- Other applications may affect multimedia timer resolution
- Try adjusting the resolution value for better precision

**High CPU usage:**
- Consider increasing the timer period
- Ensure work in the timer loop is minimal
- Profile your application to identify bottlenecks

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request. For major changes, please open an issue first to discuss what you would like to change.

### Development Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/HakanL/Haukcode.HighResolutionTimer.git
   ```

2. Build the project:
   ```bash
   cd Haukcode.HighResolutionTimer/src
   dotnet build
   ```

3. Run tests (if available):
   ```bash
   dotnet test
   ```

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Uses Windows Multimedia Timer API for high-resolution timing on Windows
- Uses Linux timerfd API for precise timing on Linux
- Uses macOS kqueue/kevent API for precise timing on macOS
- Inspired by the need for cross-platform high-resolution timing in .NET applications

## Support

- **Issues**: [GitHub Issues](https://github.com/HakanL/Haukcode.HighResolutionTimer/issues)
- **NuGet Package**: [Haukcode.HighResolutionTimer](https://www.nuget.org/packages/Haukcode.HighResolutionTimer/)

## Changelog

### Version 1.2.0
- Support for floating-point period on Linux
- Improved precision for sub-millisecond timing

### Version 1.1.0
- Added support for Linux 64-bit
- Separate implementation for better performance on 64-bit systems

### Version 1.0.0
- Initial release
- Windows support via Multimedia Timer
- Linux support via timerfd
- .NET Standard 2.0 compatibility
