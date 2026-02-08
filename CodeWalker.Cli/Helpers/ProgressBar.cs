using System;
using System.IO;
using System.Security;

#if TESTING
using System.Reflection;
using System.Threading.Tasks;

using Xunit;
#endif

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

#if TESTING
public sealed class ProgressBarTests
{
    private static int GetCurrent(ProgressBar bar) =>
        (int)typeof(ProgressBar)
            .GetField("_current", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(bar)!;

    private static bool GetEnabled(ProgressBar bar) =>
        (bool)typeof(ProgressBar)
            .GetField("_enabled", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(bar)!;

    private static void ResetThrottle(ProgressBar bar) =>
        typeof(ProgressBar)
            .GetField("_lastUpdate", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(bar, DateTime.MinValue);

    // ── Disabled-state tests ──────────────────────────────────────────

    [Fact]
    public void Constructor_disabled_when_enabled_is_false()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(100, enabled: false, sw);
        Assert.False(GetEnabled(bar));
        Assert.Equal("", sw.ToString());
    }

    [Fact]
    public void Constructor_disabled_when_total_is_zero()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(0, enabled: true, sw);
        Assert.False(GetEnabled(bar));
    }

    [Fact]
    public void Constructor_disabled_when_total_is_negative()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(-5, enabled: true, sw);
        Assert.False(GetEnabled(bar));
    }

    // ── Enabled-state tests ───────────────────────────────────────────

    [Fact]
    public void Constructor_enabled_with_custom_writer()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(100, enabled: true, sw);
        Assert.True(GetEnabled(bar));
    }

    [Fact]
    public void Constructor_renders_initial_zero_percent()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(100, enabled: true, sw, windowWidth: 120);
        string output = sw.ToString();
        Assert.StartsWith("[", output);
        Assert.Contains("(0/100)", output);
        Assert.Contains(">", output); // cursor indicator at start
    }

    // ── Render format tests ───────────────────────────────────────────

    [Fact]
    public void Render_at_50_percent_has_half_filled_bar()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(100, enabled: true, sw, windowWidth: 120);
        sw.GetStringBuilder().Clear();
        bar.Update(100 / 2); // 50 == total bypasses throttle? No, 50 < 100. Need to reset throttle.
        ResetThrottle(bar);
        bar.Update(50);
        string output = sw.ToString();
        // 50% => filled = (int)(0.5 * 40) = 20
        Assert.Contains(new string('=', 20) + ">", output);
        Assert.Contains("(50/100)", output);
    }

    [Fact]
    public void Render_at_100_percent_has_full_bar_no_cursor()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(10, enabled: true, sw, windowWidth: 120);
        sw.GetStringBuilder().Clear();
        bar.Update(10); // == total, bypasses throttle
        string output = sw.ToString();
        Assert.Contains(new string('=', 40) + "]", output);
        Assert.DoesNotContain(">", output);
        Assert.Contains("(10/10)", output);
    }

    // ── File name tests ───────────────────────────────────────────────

    [Fact]
    public void Render_shows_current_file()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(10, enabled: true, sw, windowWidth: 120);
        sw.GetStringBuilder().Clear();
        bar.Update(10, "textures/player.ytd"); // bypasses throttle at total
        string output = sw.ToString();
        Assert.Contains("textures/player.ytd", output);
    }

    [Fact]
    public void Render_truncates_long_file_with_ellipsis()
    {
        StringWriter sw = new();
        // windowWidth=80 → maxLen = Max(10, 80-40-30) = 10
        using ProgressBar bar = new(10, enabled: true, sw, windowWidth: 80);
        sw.GetStringBuilder().Clear();
        bar.Update(10, "very/long/path/to/some/deeply/nested/file.ytd");
        string output = sw.ToString();
        Assert.Contains("...", output);
        Assert.DoesNotContain("very/long/path", output);
    }

    [Fact]
    public void Render_shows_short_file_without_truncation()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(10, enabled: true, sw, windowWidth: 200);
        sw.GetStringBuilder().Clear();
        bar.Update(10, "short.ytd");
        string output = sw.ToString();
        Assert.Contains("short.ytd", output);
        Assert.DoesNotContain("...", output);
    }

    // ── State tracking tests ──────────────────────────────────────────

    [Fact]
    public void Update_sets_current_value()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(100, enabled: true, sw);
        bar.Update(42);
        Assert.Equal(42, GetCurrent(bar));
    }

    [Fact]
    public void Increment_advances_by_one()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(100, enabled: true, sw);
        bar.Increment();
        bar.Increment();
        bar.Increment();
        Assert.Equal(3, GetCurrent(bar));
    }

    [Fact]
    public void Update_and_Increment_can_interleave()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(100, enabled: true, sw);
        bar.Update(10);
        bar.Increment();
        Assert.Equal(11, GetCurrent(bar));
    }

    // ── Throttle tests ────────────────────────────────────────────────

    [Fact]
    public void Throttle_skips_rapid_updates()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(100, enabled: true, sw, windowWidth: 80);
        sw.GetStringBuilder().Clear();
        // Rapid updates — only the first and last should render
        for (int i = 1; i <= 50; i++)
            bar.Update(i);
        string output = sw.ToString();
        // We should see (1/100) from the first un-throttled call
        // but NOT every intermediate value
        Assert.Contains("(1/100)", output);
        Assert.DoesNotContain("(2/100)", output);
    }

    [Fact]
    public void Throttle_bypassed_at_total()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(10, enabled: true, sw, windowWidth: 80);
        sw.GetStringBuilder().Clear();
        // Update to total always renders even within throttle window
        bar.Update(10);
        Assert.Contains("(10/10)", sw.ToString());
    }

    [Fact]
    public void Throttle_reset_allows_render()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(100, enabled: true, sw, windowWidth: 80);
        sw.GetStringBuilder().Clear();
        ResetThrottle(bar);
        bar.Update(25);
        Assert.Contains("(25/100)", sw.ToString());
    }

    // ── Dispose tests ─────────────────────────────────────────────────

    [Fact]
    public void Dispose_writes_newline_when_enabled()
    {
        StringWriter sw = new();
        ProgressBar bar = new(10, enabled: true, sw, windowWidth: 80);
        bar.Dispose();
        Assert.EndsWith(sw.NewLine, sw.ToString());
    }

    [Fact]
    public void Dispose_does_not_throw_when_disabled()
    {
        ProgressBar bar = new(10, enabled: false, new StringWriter());
        bar.Dispose();
    }

    [Fact]
    public void Dispose_can_be_called_multiple_times()
    {
        ProgressBar bar = new(10, enabled: true, new StringWriter());
        bar.Dispose();
        bar.Dispose();
    }

    // ── Thread safety tests ───────────────────────────────────────────

    [Fact]
    public void Concurrent_increments_are_thread_safe()
    {
        const int total = 10_000;
        StringWriter sw = new();
        using ProgressBar bar = new(total, enabled: true, sw);

        Parallel.For(0, total, _ => bar.Increment());

        Assert.Equal(total, GetCurrent(bar));
    }

    [Fact]
    public void Concurrent_updates_do_not_throw()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(1000, enabled: true, sw);

        Parallel.For(0, 1000, i => bar.Update(i, $"file_{i}.txt"));

        int current = GetCurrent(bar);
        Assert.InRange(current, 0, 999);
    }

    // ── Full lifecycle test ───────────────────────────────────────────

    [Fact]
    public void Full_lifecycle_renders_progress_to_completion()
    {
        StringWriter sw = new();
        using ProgressBar bar = new(5, enabled: true, sw, windowWidth: 120);
        for (int i = 0; i < 5; i++)
        {
            ResetThrottle(bar);
            bar.Increment($"step_{i}");
        }
        string output = sw.ToString();
        Assert.Contains("(5/5)", output);
        Assert.Equal(5, GetCurrent(bar));
    }
}
#endif
