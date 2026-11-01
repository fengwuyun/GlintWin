using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
namespace GlintWin;
/// <summary>Connects stable OCR snapshots to explicit selection signals without persisting raw keys.</summary>
public sealed class DailyCollectionService : IDisposable
{
    private readonly Config _config;
    private readonly Func<bool> _paused;
    private readonly Lazy<DailyCollector> _collector;
    private readonly Lazy<DailyWordStore> _store;
    private readonly object _gate = new();
    private long _cachedWindow;
    private bool _cachedAllowed;
    private long _generation, _observedGeneration;
    public DailyWordStore Store => _store.Value;
    public DailyCollectionService(Config config, Func<bool> paused)
    {
        _config = config; _paused = paused;
        string databasePath = Path.Combine(AppPaths.Root, "daily-words.db");
        _store = new Lazy<DailyWordStore>(() => new DailyWordStore(databasePath));
        _collector = new Lazy<DailyCollector>(() => new DailyCollector(Store));
        PinyinTracker.CandidateConfirmed += Confirm;
        PinyinTracker.CompositionInvalidated += Cancel;
    }
    public void Observe(string word,string pinyin,string? meaning,long foreground,long nowMs)
        => ObserveCandidates(new[] { (word, meaning) },pinyin,foreground,nowMs);
    public void ObserveCandidates(IReadOnlyList<(string Word,string? Meaning)> candidates,string pinyin,long foreground,long nowMs)
    {
        long generation = Interlocked.Read(ref _generation);
        bool enabled = _config.AutoCollectDailyInput && !_paused();
        bool allowed = enabled && Allowed(foreground);
        if (!enabled || !allowed) { Cancel(); return; }
        var collector = _collector.Value; // Disk initialization stays outside the keyboard-facing gate.
        lock (_gate)
        {
            collector.Enabled = enabled;
            _cachedWindow = foreground; _cachedAllowed = allowed;
            if (!enabled || !allowed || generation != Interlocked.Read(ref _generation)) { collector.Cancel(); return; }
            _observedGeneration = generation;
            collector.ObserveCandidates(candidates,pinyin,foreground,nowMs);
        }
    }
    private void Confirm(int index,string pinyin,long foreground,long nowMs)
    {
        // Never wait behind process inspection or SQLite operations in a keyboard hook.
        if (!Monitor.TryEnter(_gate)) { Interlocked.Increment(ref _generation); return; }
        try
        {
            if (!_collector.IsValueCreated) return;
            var collector = _collector.Value;
            collector.Enabled = _config.AutoCollectDailyInput && !_paused();
            if (!collector.Enabled || foreground != _cachedWindow || !_cachedAllowed || _observedGeneration != Interlocked.Read(ref _generation))
            { collector.Cancel(); return; }
            collector.Confirm(index,pinyin,foreground,nowMs);
        }
        finally { Interlocked.Increment(ref _generation); Monitor.Exit(_gate); }
    }
    public void Cancel() { Interlocked.Increment(ref _generation); if (_collector.IsValueCreated) _collector.Value.Cancel(); }
    public void RefreshEnabled() { Cancel(); if (_collector.IsValueCreated) _collector.Value.Enabled = _config.AutoCollectDailyInput && !_paused(); }
    private bool Allowed(long foreground)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(new IntPtr(foreground), out var pid);
            if (pid == 0 || pid == Environment.ProcessId) return false;
            using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName;
            return !(_config.CollectionExcludedApps ?? new()).Any(x =>
                string.Equals(Path.GetFileNameWithoutExtension(x.Trim()), name, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }
    public void Dispose()
    {
        PinyinTracker.CandidateConfirmed -= Confirm; PinyinTracker.CompositionInvalidated -= Cancel;
        Cancel(); if (_store.IsValueCreated) _store.Value.Dispose();
    }
}
