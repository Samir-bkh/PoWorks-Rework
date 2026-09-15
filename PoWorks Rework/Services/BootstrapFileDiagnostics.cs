using System.Runtime.CompilerServices;
using System.Text;

namespace PoWorks_Rework.Services
{
    public static class BootstrapFileDiagnostics
    {
        private static readonly object Sync = new();
        private static TextWriter? _originalOut;
        private static TextWriter? _originalError;
        private static DailyLogWriter? _writer;

        [ModuleInitializer]
        public static void Initialize()
        {
            try
            {
                var root = FileDiagnostics.ResolveLogRoot();
                _writer = new DailyLogWriter(root);

                _originalOut = Console.Out;
                _originalError = Console.Error;

                Console.SetOut(new TeeTextWriter(_originalOut, _writer, "STDOUT"));
                Console.SetError(new TeeTextWriter(_originalError, _writer, "STDERR"));

                AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                {
                    if (args.ExceptionObject is Exception ex)
                        WriteEmergency("UNHANDLED", ex.ToString());
                    else
                        WriteEmergency("UNHANDLED", args.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
                };

                TaskScheduler.UnobservedTaskException += (_, args) =>
                {
                    WriteEmergency("UNOBSERVED_TASK", args.Exception.ToString());
                };

                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    lock (Sync)
                    {
                        _writer?.Dispose();
                    }
                };

                WriteEmergency("STARTUP", $"PoWorks process started. PID={Environment.ProcessId}");
            }
            catch
            {
                // Diagnostics must never stop the application from starting.
            }
        }

        private static void WriteEmergency(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    _writer?.WriteStamped(level, message);
                }
            }
            catch
            {
            }
        }

        private sealed class TeeTextWriter : TextWriter
        {
            private readonly TextWriter _console;
            private readonly DailyLogWriter _file;
            private readonly string _source;

            public TeeTextWriter(TextWriter console, DailyLogWriter file, string source)
            {
                _console = console;
                _file = file;
                _source = source;
            }

            public override Encoding Encoding => _console.Encoding;

            public override void Write(char value)
            {
                _console.Write(value);
                _file.WriteRaw(value.ToString());
            }

            public override void Write(string? value)
            {
                _console.Write(value);
                if (value != null)
                    _file.WriteRaw(value);
            }

            public override void WriteLine(string? value)
            {
                _console.WriteLine(value);
                _file.WriteStamped(_source, value ?? string.Empty);
            }

            public override void Flush()
            {
                _console.Flush();
                _file.Flush();
            }
        }

        private sealed class DailyLogWriter : TextWriter
        {
            private readonly string _root;
            private StreamWriter? _stream;
            private DateTime _streamDate;

            public DailyLogWriter(string root)
            {
                _root = root;
                Directory.CreateDirectory(root);
            }

            public override Encoding Encoding => Encoding.UTF8;

            public void WriteStamped(string source, string value)
            {
                lock (Sync)
                {
                    EnsureStream();
                    _stream!.WriteLine($"{DateTimeOffset.UtcNow:O} [{source}] {value}");
                    _stream.Flush();
                }
            }

            public void WriteRaw(string value)
            {
                lock (Sync)
                {
                    EnsureStream();
                    _stream!.Write(value);
                    _stream.Flush();
                }
            }

            public override void Flush()
            {
                lock (Sync)
                {
                    _stream?.Flush();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    lock (Sync)
                    {
                        _stream?.Dispose();
                        _stream = null;
                    }
                }

                base.Dispose(disposing);
            }

            private void EnsureStream()
            {
                var today = DateTime.UtcNow.Date;
                if (_stream != null && _streamDate == today)
                    return;

                _stream?.Dispose();
                _streamDate = today;
                var path = Path.Combine(_root, $"poworks-{today:yyyy-MM-dd}.log");
                _stream = new StreamWriter(
                    new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                    new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
            }
        }
    }
}
