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

            var local = Path.Combine(AppContext.BaseDirectory, "logs");
            try
            {
                Directory.CreateDirectory(local);
                return local;
            }
            catch
            {
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var fallback = Path.Combine(
                    string.IsNullOrWhiteSpace(programData) ? AppContext.BaseDirectory : programData,
                    "PoWorks",
                    "Logs");
                Directory.CreateDirectory(fallback);
                return fallback;
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
    }

    public sealed class PoWorksFileLoggerProvider : ILoggerProvider
    {
        private readonly string _root;
        private readonly int _retentionDays;
        private readonly object _sync = new();

        public PoWorksFileLoggerProvider(IConfiguration configuration)
        {
            _root = FileDiagnostics.ResolveLogRoot(configuration);
            _retentionDays = int.TryParse(configuration["Diagnostics:RetentionDays"], out var days)
                ? Math.Max(1, days)
                : 30;

            CleanupOldLogs();
        }

        public ILogger CreateLogger(string categoryName) => new PoWorksFileLogger(categoryName, _root, _sync);

        public void Dispose()
        {
        }

        private void CleanupOldLogs()
        {
            try
            {
                var threshold = DateTime.UtcNow.Date.AddDays(-_retentionDays);
                foreach (var file in Directory.EnumerateFiles(_root, "poworks-*.log"))
                {
                    if (File.GetLastWriteTimeUtc(file) < threshold)
                        File.Delete(file);
                }
            }
            catch
            {
                // Logging cleanup is best effort only.
            }
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
