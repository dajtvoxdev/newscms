using Microsoft.Extensions.Logging;

namespace AdVideo.Tests.Infrastructure;

/// <summary>
/// Gom mọi dòng log của worker vào một danh sách để test đọc được.
/// </summary>
/// <remarks>
/// Pipeline có cả một lớp sự cố <b>không</b> làm job đỏ: shot cắt giữa câu, không trích được tiếng
/// gốc, QC trượt phép kiểm không chặn. Những thứ đó chỉ tồn tại dưới dạng một dòng log; không đọc
/// được log thì test không phân biệt được "chạy đúng" với "chạy xong bất chấp".
/// </remarks>
public sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly List<string> _lines;

    /// <summary>Tạo provider ghi vào danh sách cho trước.</summary>
    public ListLoggerProvider(List<string> lines) => _lines = lines;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new ListLogger(_lines, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class ListLogger : ILogger
    {
        private readonly List<string> _lines;
        private readonly string _category;

        public ListLogger(List<string> lines, string category)
        {
            _lines = lines;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            string line = $"{logLevel} {_category}: {formatter(state, exception)}";

            if (exception is not null)
            {
                line += $" | {exception.GetType().Name}: {exception.Message}";
            }

            // Pipeline chạy tuần tự nhưng HttpClientFactory và ffmpeg ghi log từ thread khác.
            lock (_lines)
            {
                _lines.Add(line);
            }
        }
    }
}
