using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ObjectStorage;

internal sealed class MetricsCollector(ILogger<Storage> logger)
{
    private readonly Stopwatch _performanceMonitor = Stopwatch.StartNew();
    private long _totalBytesWritten;

    public void RecordBytesWritten(int bytes)
    {
        Interlocked.Add(ref _totalBytesWritten, bytes);
    }

    public void RecordFinalMetrics()
    {
        _performanceMonitor.Stop();

        var throughput = _totalBytesWritten / 1024.0 / 1024.0 /
                         (_performanceMonitor.ElapsedMilliseconds / 1000.0);

        logger?.LogInformation(
            "Stream metrics - Total bytes: {TotalBytes}, Throughput: {Throughput:F2} MB/s",
            _totalBytesWritten,
            throughput);
    }
}