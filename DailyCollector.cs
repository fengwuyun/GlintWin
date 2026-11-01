using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GlintWin;

/// <summary>Matches an explicit first-candidate selection to a recent stable snapshot.</summary>
public sealed class DailyCollector
{
    private readonly DailyWordStore _store;
    private readonly object _gate = new();
    private bool _enabled;
    private Snapshot? _snapshot;
    private sealed record Snapshot((string Word, string? Meaning)[] Candidates, string Pinyin, long Foreground, long At);
    public DailyCollector(DailyWordStore store) => _store = store;
    public bool Enabled { get { lock (_gate) return _enabled; } set { lock (_gate) { _enabled = value; if (!value) _snapshot = null; } } }
    private static string Normalize(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
    public void Observe(string word, string pinyin, string? meaning, long foreground, long nowMs)
        => ObserveCandidates(new[] { (word, meaning) }, pinyin, foreground, nowMs);
    public void ObserveCandidates(IReadOnlyList<(string Word, string? Meaning)> candidates, string pinyin, long foreground, long nowMs)
    {
        lock (_gate)
        {
            _snapshot = null;
            if (!_enabled || candidates == null || candidates.Count == 0 || candidates.Count > 9 || string.IsNullOrWhiteSpace(pinyin) || foreground == 0) return;
            var copy = new (string Word, string? Meaning)[candidates.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                if (!DailyWordStore.ValidWord(candidates[i].Word)) return;
                copy[i] = (candidates[i].Word.Trim(), candidates[i].Meaning);
            }
            _snapshot = new(copy, Normalize(pinyin), foreground, nowMs);
        }
    }
    public void Confirm(int candidateIndex, string pinyin, long foreground, long nowMs)
    {
        lock (_gate)
        {
            var snapshot = _snapshot;
            _snapshot = null;
            if (!_enabled || snapshot == null || candidateIndex < 1 || candidateIndex > snapshot.Candidates.Length || string.IsNullOrWhiteSpace(pinyin)
                || foreground != snapshot.Foreground || nowMs < snapshot.At || nowMs - snapshot.At > 1200
                || Normalize(pinyin) != snapshot.Pinyin) return;
            var selected = snapshot.Candidates[candidateIndex - 1];
            _store.Record(selected.Word, snapshot.Pinyin, selected.Meaning, true);
        }
    }
    public void Cancel() { lock (_gate) _snapshot = null; }
}
