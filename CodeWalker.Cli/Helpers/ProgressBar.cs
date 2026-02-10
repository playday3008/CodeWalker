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
        this._total = total;
        this._writer = writer ?? Console.Error;
        this._ownsConsole = writer is null;
        this._windowWidth = windowWidth;
        this._enabled = enabled && total > 0 && (!this._ownsConsole || !Console.IsErrorRedirected);
        if (this._enabled)
        {
            if (this._ownsConsole)
            {
                try
                {
                    Console.CursorVisible = false;
                }
                catch { }
            }
            this.Render();
        }
    }

    /// <summary>
    /// Updates the progress bar to the specified current value.
    /// </summary>
    /// <param name="current">Current number of items processed.</param>
    /// <param name="currentFile">Optional current file being processed.</param>
    public void Update(int current, string? currentFile = null)
    {
        lock (this._lock)
        {
            this._current = current;
            if (!this._enabled)
                return;

            // Throttle updates to avoid flickering
            if ((DateTime.Now - this._lastUpdate).TotalMilliseconds < 50 && current < this._total)
                return;

            this._lastUpdate = DateTime.Now;
            this.Render(currentFile);
        }
    }

    /// <summary>
    /// Increments the progress bar by one.
    /// Thread-safe: the increment and render happen atomically under a lock.
    /// </summary>
    /// <param name="currentFile">Optional current file being processed.</param>
    public void Increment(string? currentFile = null)
    {
        lock (this._lock)
        {
            this._current++;
            if (!this._enabled)
                return;

            if ((DateTime.Now - this._lastUpdate).TotalMilliseconds < 50 && this._current < this._total)
                return;

            this._lastUpdate = DateTime.Now;
            this.Render(currentFile);
        }
    }

    private void Render(string? currentFile = null)
    {
        if (!this._enabled)
            return;

        try
        {
            double percent = this._total > 0 ? (double)this._current / this._total : 0;
            int filled = Math.Min((int)(percent * this._barWidth), this._barWidth);
            int winWidth = this._ownsConsole ? Console.WindowWidth : this._windowWidth;

            if (this._ownsConsole)
                Console.SetCursorPosition(0, Console.CursorTop);

            this._writer.Write("[");
            this._writer.Write(new string('=', filled));
            if (filled < this._barWidth)
            {
                this._writer.Write(">");
                this._writer.Write(new string(' ', this._barWidth - filled - 1));
            }

            string stats = $"] {percent,6:P0} ({this._current}/{this._total})";
            this._writer.Write(stats);

            int written = 1 + this._barWidth + stats.Length;

            if (!string.IsNullOrEmpty(currentFile))
            {
                int maxLen = Math.Max(10, winWidth - this._barWidth - 30);
                string displayFile =
                    currentFile!.Length > maxLen
                        ? $"...{currentFile[(currentFile.Length - maxLen + 3)..]}"
                        : currentFile;
                string fileText = $" {displayFile}";
                this._writer.Write(fileText);
                written += fileText.Length;
            }

            // Clear rest of line
            int remaining = winWidth - written - 1;
            if (remaining > 0)
            {
                this._writer.Write(new string(' ', remaining));
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
        if (this._enabled)
        {
            try
            {
                this._writer.WriteLine();
                if (this._ownsConsole)
                    Console.CursorVisible = true;
            }
            catch (Exception ex)
                when (ex is IOException or InvalidOperationException or SecurityException)
            { }
        }
    }
}
