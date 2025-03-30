using Serilog;
using Serilog.Events;

namespace DxvkVersionManager.Services.Implementations;

public class LoggingService
{
    private static LoggingService? _instance;
    private readonly ILogger _logger;

    private LoggingService()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "dxvk-manager.log");
        
        // Ensure logs directory exists
        var logsDir = Path.GetDirectoryName(logPath);
        if (!Directory.Exists(logsDir))
        {
            Directory.CreateDirectory(logsDir!);
        }

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, 
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static LoggingService Instance
    {
        get
        {
            _instance ??= new LoggingService();
            return _instance;
        }
    }

    public void LogDebug(string message) => _logger.Debug(message);
    public void LogInformation(string message) => _logger.Information(message);
    public void LogWarning(string message) => _logger.Warning(message);
    public void LogWarning(Exception ex, string message) => _logger.Warning(ex, message);
    public void LogError(string message) => _logger.Error(message);
    public void LogError(Exception ex, string message) => _logger.Error(ex, message);
    public void LogCritical(string message) => _logger.Fatal(message);
    public void LogCritical(Exception ex, string message) => _logger.Fatal(ex, message);
} 