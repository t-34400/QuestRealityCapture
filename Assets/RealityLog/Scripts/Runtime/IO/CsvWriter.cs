# nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RealityLog
{
    public class CsvWriter : IDisposable
    {
        private readonly BlockingCollection<string[]> _queue = new();
        private readonly string _filePath;
        private readonly string[] _header;
        private readonly Task _writerTask;
        private bool _disposed = false;

        public CsvWriter(string filePath, string[]? header = null)
        {
            _filePath = filePath;
            _header = header ?? Array.Empty<string>();
            _writerTask = Task.Run(WriteLoop);
        }

        public void EnqueueRow(params double[] columns)
            => EnqueueRow(columns.Select(f => f.ToString()).ToArray());
        public void EnqueueRow(params string[] columns)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CsvWriter));
            _queue.Add(columns);
        }

        private void WriteLoop()
        {
            var directoryName = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(_filePath))
            {
                Directory.CreateDirectory(directoryName);
            }

            using var writer = new StreamWriter(_filePath, append: false);

            if (_header.Length > 0)
            {
                writer.WriteLine(string.Join(",", _header));
            }

            foreach (var row in _queue.GetConsumingEnumerable())
            {
                writer.WriteLine(string.Join(",", row));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _queue.CompleteAdding();
            _writerTask.Wait();
            _disposed = true;
        }
    }
}
