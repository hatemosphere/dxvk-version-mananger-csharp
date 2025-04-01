using Serilog;
using Serilog.Events;

namespace DxvkVersionManager.Services.Implementations;

// Singleton Logging Service
public sealed class LoggingService
{
    private static readonly Lazy<LoggingService> _instance = new(() => new LoggingService());
    private readonly ILogger _logger; // Use Serilog's ILogger

    private LoggingService()
    {
        // Get the globally configured Serilog logger
        _logger = Log.Logger;
    }

    public static LoggingService Instance => _instance.Value;

    public void LogInformation(string message)
    {
        _logger.Information(message);
    }

    public void LogWarning(string message)
    {
        _logger.Warning(message);
    }

    public void LogError(Exception ex, string message)
    {
        _logger.Error(ex, message);
    }

    public void LogDebug(string message)
    {
        _logger.Debug(message);
    }

    public void LogWarning(Exception ex, string message) => _logger.Warning(ex, message);
    public void LogError(string message) => _logger.Error(message);
    public void LogCritical(string message) => _logger.Fatal(message);
    public void LogCritical(Exception ex, string message) => _logger.Fatal(ex, message);
} 