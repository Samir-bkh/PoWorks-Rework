using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace PoWorks_Rework.Services
{
    public static class FileDiagnostics
    {
        private static readonly object Sync = new();

        public static string ResolveLogRoot(IConfiguration? configuration = null)
        {
            var configured = configuration?["Diagnostics:LogDirectory"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var expanded = Environment.ExpandEnvironmentVariables(configured);
                Directory.CreateDirectory(expanded);
                return Path.GetFullPath(expanded);
            }

            // On Windows, keep diagnostics outside the application directory so logs survive
            // an application update/redeployment and remain available even when PoWorks cannot start.
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    if (!string.IsNullOrWhiteSpace(programData))
                    {
                        var persistent = Path.Combine(programData, "PoWorks", "Logs");
                        Directory.CreateDirectory(persistent);
                        return persistent;
                    }
                }
                catch
                {
                    // Fall through to an application-local directory if ProgramData is unavailable.
                }
            }

            var local = Path.Combine(AppContext.BaseDirectory, "logs");
            try
            {
                Directory.CreateDirectory(local);
                return local;
            }
            catch
            {
                var temp = Path.Combine(Path.GetTempPath(), "PoWorks", "Logs");
                Directory.CreateDirectory(temp);
                return temp;
            }
        }

        public static void WriteEmergency(Exception exception, string stage, IConfiguration? configuration = null)
        {
            try
            {
                var root = ResolveLogRoot(configuration);
                var path = Path.Combine(root, $"poworks-{DateTime.UtcNow:yyyy-MM-dd}.log");
                var line = $"{DateTimeOffset.UtcNow:O} [CRITICAL] [Startup] {stage}{Environment.NewLine}{exception}{Environment.NewLine}";
                lock (Sync)
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Emergency logging must never hide the original failure.
            }
        }

        public static void CleanupOldLogs(IConfiguration? configuration = null)
        {
            try
            {
                var root = ResolveLogRoot(configuration);
                var retentionDays = int.TryParse(configuration?["Diagnostics:RetentionDays"], out var configuredDays)
                    ? Math.Max(1, configuredDays)
                    : 30;
                var threshold = DateTime.UtcNow.Date.AddDays(-retentionDays);

                DeleteOlderThan(root, "poworks-*.log", threshold);
                DeleteOlderThan(root, "console-*.log", threshold);

                var auditDirectory = Path.Combine(root, "audit");
                if (Directory.Exists(auditDirectory))
                    DeleteOlderThan(auditDirectory, "audit-*.jsonl", threshold);
            }
            catch
            {
                // Logging cleanup is best effort only.
            }
        }

        private static void DeleteOlderThan(string directory, string pattern, DateTime threshold)
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern))
            {
                if (File.GetLastWriteTimeUtc(file) < threshold)
                    File.Delete(file);
            }
        }
    }

    public sealed class PoWorksFileLoggerProvider : ILoggerProvider
    {
        private readonly string _root;
        private readonly object _sync = new();

        public PoWorksFileLoggerProvider(IConfiguration configuration)
        {
            _root = FileDiagnostics.ResolveLogRoot(configuration);
            FileDiagnostics.CleanupOldLogs(configuration);
        }

        public ILogger CreateLogger(string categoryName) => new PoWorksFileLogger(categoryName, _root, _sync);

        public void Dispose()
        {
        }
    }

    internal sealed class PoWorksFileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _root;
        private readonly object _sync;

        public PoWorksFileLogger(string category, string root, object sync)
        {
            _category = category;
            _root = root;
            _sync = sync;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            try
            {
                var path = Path.Combine(_root, $"poworks-{DateTime.UtcNow:yyyy-MM-dd}.log");
                var message = formatter(state, exception);
                var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] [{_category}] {message}";
                if (exception != null)
                    line += Environment.NewLine + exception;

                lock (_sync)
                {
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // File logging must never crash the application.
            }
        }
    }
}
