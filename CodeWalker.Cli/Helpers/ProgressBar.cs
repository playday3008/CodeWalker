using System;
using System.IO;
using System.Security;

namespace CodeWalker.Cli.Helpers;

/// <summary>
/// Displays a console progress bar on stderr to keep stdout clean for data/JSON output.
/// </summary>
internal sealed class ProgressBar : IDisposable
{
    private readonly int _total;
    private int _current;
    private readonly bool _enabled;
    private readonly int _barWidth = 40;
    private readonly TextWriter _writer;
    private readonly bool _ownsConsole;
    private readonly int _windowWidth;
    private DateTime _lastUpdate = DateTime.MinValue;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the ProgressBar class.
    /// </summary>
    /// <param name="total">Total number of items to process.</param>
    /// <param name="enabled">Whether to enable the progress bar display.</param>
    /// <param name="writer">Optional text writer for output. When null, writes to stderr with console cursor control.</param>
    /// <param name="windowWidth">Terminal width used for padding and truncation when a custom writer is provided.</param>
    public ProgressBar(int total, bool enabled, TextWriter? writer = null, int windowWidth = 120)
    {
        _total = total;
        _writer = writer ?? Console.Error;
        _ownsConsole = writer is null;
        _windowWidth = windowWidth;
        _enabled = enabled && total > 0 && (!_ownsConsole || !Console.IsErrorRedirected);
        if (_enabled)
        {
            if (_ownsConsole)
            {
                try
                {
                    Console.CursorVisible = false;
                }
                catch { }
            }
            Render();
        }
    }

    /// <summary>
    /// Updates the progress bar to the specified current value.
    /// </summary>
    /// <param name="current">Current number of items processed.</param>
    /// <param name="currentFile">Optional current file being processed.</param>
    public void Update(int current, string? currentFile = null)
    {
        lock (_lock)
        {
            _current = current;
            if (!_enabled)
                return;

            // Throttle updates to avoid flickering
            if ((DateTime.Now - _lastUpdate).TotalMilliseconds < 50 && current < _total)
                return;

            _lastUpdate = DateTime.Now;
            Render(currentFile);
        }
    }

    /// <summary>
    /// Increments the progress bar by one.
    /// Thread-safe: the increment and render happen atomically under a lock.
    /// </summary>
    /// <param name="currentFile">Optional current file being processed.</param>
    public void Increment(string? currentFile = null)
    {
        lock (_lock)
        {
            _current++;
            if (!_enabled)
                return;

            if ((DateTime.Now - _lastUpdate).TotalMilliseconds < 50 && _current < _total)
                return;

            _lastUpdate = DateTime.Now;
            Render(currentFile);
        }
    }

    private void Render(string? currentFile = null)
    {
        if (!_enabled)
            return;

        try
        {
            double percent = _total > 0 ? (double)_current / _total : 0;
            int filled = Math.Min((int)(percent * _barWidth), _barWidth);
            int winWidth = _ownsConsole ? Console.WindowWidth : _windowWidth;

            if (_ownsConsole)
                Console.SetCursorPosition(0, Console.CursorTop);

            _writer.Write("[");
            _writer.Write(new string('=', filled));
            if (filled < _barWidth)
            {
                _writer.Write(">");
                _writer.Write(new string(' ', _barWidth - filled - 1));
            }

            string stats = $"] {percent,6:P0} ({_current}/{_total})";
            _writer.Write(stats);

            int written = 1 + _barWidth + stats.Length;

            if (!string.IsNullOrEmpty(currentFile))
            {
                int maxLen = Math.Max(10, winWidth - _barWidth - 30);
                string displayFile =
                    currentFile!.Length > maxLen
                        ? $"...{currentFile[(currentFile.Length - maxLen + 3)..]}"
                        : currentFile;
                string fileText = $" {displayFile}";
                _writer.Write(fileText);
                written += fileText.Length;
            }

            // Clear rest of line
            int remaining = winWidth - written - 1;
            if (remaining > 0)
            {
                _writer.Write(new string(' ', remaining));
            }
        }
        catch (Exception ex)
            when (ex is IOException or InvalidOperationException or SecurityException)
        {
            // Ignore console errors (e.g. redirected output, no terminal)
        }
    }

    /// <summary>
    /// Disposes the progress bar, ensuring the console state is restored.
    /// </summary>
    public void Dispose()
    {
        if (_enabled)
        {
            try
            {
                _writer.WriteLine();
                if (_ownsConsole)
                    Console.CursorVisible = true;
            }
            catch (Exception ex)
                when (ex is IOException or InvalidOperationException or SecurityException)
            { }
        }
    }
}
