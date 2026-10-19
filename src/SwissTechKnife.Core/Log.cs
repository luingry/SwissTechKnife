using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace SwissTechKnife.Core
{
    /// <summary>
    /// File logger. Callers only enqueue (never block on disk); one background thread writes.
    /// Rotates host.log to host.1.log at 1 MB.
    /// </summary>
    public static class Log
    {
        private const long MaxBytes = 1024 * 1024;
        private static readonly BlockingCollection<string> Queue = new BlockingCollection<string>(4096);
        private static string _path;
        private static Thread _writer;

        public static void Start(string directory)
        {
            if (_writer != null)
            {
                return;
            }
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "host.log");
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "Log writer", Priority = ThreadPriority.BelowNormal };
            _writer.Start();
        }

        public static void Info(string source, string message) => Write("INFO", source, message, null);

        public static void Warn(string source, string message) => Write("WARN", source, message, null);

        public static void Error(string source, string message, Exception exception = null) => Write("ERROR", source, message, exception);

        public static void Flush()
        {
            Queue.CompleteAdding();
            _writer?.Join(1000);
        }

        private static void Write(string level, string source, string message, Exception exception)
        {
            var line = new StringBuilder(128)
                .Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
                .Append(' ').Append(level)
                .Append(" [").Append(source).Append("] ")
                .Append(message);
            if (exception != null)
            {
                line.Append(Environment.NewLine).Append(exception);
            }
            System.Diagnostics.Debug.WriteLine(line.ToString());
            if (!Queue.IsAddingCompleted)
            {
                Queue.TryAdd(line.ToString());
            }
        }

        private static void WriteLoop()
        {
            try
            {
                foreach (string line in Queue.GetConsumingEnumerable())
                {
                    try
                    {
                        var info = new FileInfo(_path);
                        if (info.Exists && info.Length > MaxBytes)
                        {
                            string rotated = Path.Combine(info.DirectoryName, "host.1.log");
                            File.Delete(rotated);
                            File.Move(_path, rotated);
                        }
                        File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                    }
                    catch (IOException)
                    {
                        // Logging must never take the host down.
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
