using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GlintWin;

public sealed class DailyWordEntry
{
    public long Id { get; set; }
    public string Date { get; set; } = "";
    public string Word { get; set; } = "";
    public string Pinyin { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Status { get; set; } = "待翻译";
    public int Count { get; set; }
    public bool NeedsReview { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
}

/// <summary>Local SQLite vocabulary. Hook callers only enqueue bounded in-memory records.</summary>
public sealed class DailyWordStore : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new(2048);
    private readonly object _submit = new(), _dbGate = new();
    private readonly Task _writer;
    private IntPtr _db;
    private bool _disposed;
    private string? _lastError;
    public string? LastError => Volatile.Read(ref _lastError);
    private void Error(Exception ex) => Volatile.Write(ref _lastError, ex.Message);
    public DailyWordStore(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlintWin", "daily-words.db");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            if (Native.sqlite3_open_v2(path, out _db, 6, IntPtr.Zero) != 0) throw Failure();
            Native.sqlite3_busy_timeout(_db, 5000);
            Execute("PRAGMA journal_mode=WAL");
            Execute("CREATE TABLE IF NOT EXISTS daily_words(id INTEGER PRIMARY KEY,date TEXT NOT NULL,word TEXT NOT NULL,pinyin TEXT NOT NULL,meaning TEXT NOT NULL,status TEXT NOT NULL,count INTEGER NOT NULL,needs_review INTEGER NOT NULL,first_seen TEXT NOT NULL,last_seen TEXT NOT NULL,UNIQUE(date,word))");
            Execute("CREATE INDEX IF NOT EXISTS ix_daily_words_date_status ON daily_words(date,status)");
            Execute("PRAGMA user_version=1");
        }
        catch { if (_db != IntPtr.Zero) Native.sqlite3_close_v2(_db); _db = IntPtr.Zero; throw; }
        _writer = Task.Run(() => { foreach (var action in _queue.GetConsumingEnumerable()) { try { lock (_dbGate) action(); } catch (Exception ex) { Error(ex); } } });
    }
    internal static bool ValidWord(string? word)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Trim().Length > 80) return false;
        bool letter = false;
        foreach (var c in word.Trim()) { if (char.IsControl(c) || char.IsWhiteSpace(c)) return false; letter |= char.IsLetter(c); }
        return letter;
    }
    public void Record(string word, string pinyin, string? meaning, bool needsReview, DateTimeOffset? at = null)
    {
        if (!ValidWord(word) || string.IsNullOrWhiteSpace(pinyin) || pinyin.Length > 256 || (meaning?.Length ?? 0) > 8192) return;
        var time = at ?? DateTimeOffset.Now;
        var date = time.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        word = word.Trim(); pinyin = pinyin.Trim(); meaning = meaning?.Trim() ?? "";
        var stamp = time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        // Control operations may wait for disk-backed queue progress; a hook must never wait behind them.
        if (!Monitor.TryEnter(_submit)) { Error(new InvalidOperationException("Daily vocabulary queue is busy; record dropped.")); return; }
        try
        {
            if (_disposed) { Error(new ObjectDisposedException(nameof(DailyWordStore))); return; }
            if (!_queue.TryAdd(() => Execute(@"INSERT INTO daily_words(date,word,pinyin,meaning,status,count,needs_review,first_seen,last_seen) VALUES(?,?,?,?,?,1,?,?,?)
ON CONFLICT(date,word) DO UPDATE SET count=count+1,first_seen=min(first_seen,excluded.first_seen),last_seen=max(last_seen,excluded.last_seen)", date, word, pinyin, meaning, meaning.Length == 0 ? "待翻译" : "已有释义", needsReview ? 1 : 0, stamp, stamp)))
                Error(new InvalidOperationException("Daily vocabulary queue is full; record dropped."));
        }
        finally { Monitor.Exit(_submit); }
    }
    public void Flush()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_submit) { ObjectDisposedException.ThrowIf(_disposed, this); _queue.Add(() => done.SetResult()); }
        done.Task.GetAwaiter().GetResult();
    }
    private void Serialized(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_submit)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _queue.Add(() => { try { action(); done.SetResult(); } catch (Exception ex) { Error(ex); done.SetException(ex); } });
        }
        done.Task.GetAwaiter().GetResult();
    }
    private static void CheckDate(string date)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new ArgumentException("Date must be yyyy-MM-dd.", nameof(date));
    }
    private static string Filter(string status) => status == "待核对" ? " AND needs_review=1" : status.Length == 0 ? "" : " AND status=?";
    private static object[] Args(string date, string status, string search)
    {
        var args = new List<object> { date };
        if (status.Length > 0 && status != "待核对") args.Add(status);
        args.Add(search); args.Add(search); args.Add(search);
        return args.ToArray();
    }
    private const string SearchSql = " AND (instr(word,?)>0 OR instr(pinyin,?)>0 OR instr(meaning,?)>0)";
    public List<DailyWordEntry> Query(string date, string status = "", string search = "", int page = 0, int pageSize = 50, bool sortByFrequency = false)
    {
        CheckDate(date); Flush(); var result = new List<DailyWordEntry>();
        var args = new List<object>(Args(date, status, search)) { Math.Clamp(pageSize, 1, 500), (long)Math.Max(0,page) * Math.Clamp(pageSize, 1, 500) };
        lock (_dbGate) { ObjectDisposedException.ThrowIf(_db == IntPtr.Zero, this); Run("SELECT id,date,word,pinyin,meaning,status,count,needs_review,first_seen,last_seen FROM daily_words WHERE date=?" + Filter(status) + SearchSql + (sortByFrequency ? " ORDER BY count DESC,last_seen DESC,id DESC LIMIT ? OFFSET ?" : " ORDER BY last_seen DESC,id DESC LIMIT ? OFFSET ?"), s =>
            result.Add(new DailyWordEntry { Id=Native.sqlite3_column_int64(s,0), Date=Text(s,1), Word=Text(s,2), Pinyin=Text(s,3), Meaning=Text(s,4), Status=Text(s,5), Count=(int)Native.sqlite3_column_int64(s,6), NeedsReview=Native.sqlite3_column_int64(s,7)!=0, FirstSeen=DateTimeOffset.Parse(Text(s,8),CultureInfo.InvariantCulture), LastSeen=DateTimeOffset.Parse(Text(s,9),CultureInfo.InvariantCulture) }), args.ToArray()); }
        return result;
    }
    public int Count(string date, string status = "", string search = "")
    {
        CheckDate(date); Flush(); int count=0;
        lock (_dbGate) { ObjectDisposedException.ThrowIf(_db == IntPtr.Zero, this); Run("SELECT count(*) FROM daily_words WHERE date=?" + Filter(status) + SearchSql, s => count=(int)Native.sqlite3_column_int64(s,0), Args(date,status,search)); }
        return count;
    }
    public void Update(DailyWordEntry entry)
    {
        if (!ValidWord(entry.Word) || entry.Pinyin.Length > 256 || entry.Meaning.Length > 8192 || entry.Status is not ("待翻译" or "已有释义" or "已忽略" or "已入库")) throw new ArgumentException("Invalid daily word entry.");
        // Snapshot mutable UI properties before the worker sees them.
        var id=entry.Id; var word=entry.Word.Trim(); var pinyin=entry.Pinyin; var meaning=entry.Meaning; var status=entry.Status; var review=entry.NeedsReview;
        Serialized(() => Execute("UPDATE daily_words SET word=?,pinyin=?,meaning=?,status=?,needs_review=? WHERE id=?", word,pinyin,meaning,status,review?1:0,id));
    }
    public void ExportCsv(string path, string date, string status = "", string search = "")
    {
        CheckDate(date);
        Serialized(() =>
        {
            using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
            writer.WriteLine("日期,词条,拼音,释义,次数,状态,待核对");
            // One query snapshot, streamed directly to disk, so concurrent recording cannot shift pages.
            Run("SELECT date,word,pinyin,meaning,count,status,needs_review FROM daily_words WHERE date=?" + Filter(status) + SearchSql + " ORDER BY last_seen DESC,id DESC", s =>
            {
                var values = new[] { Text(s,0),Text(s,1),Text(s,2),Text(s,3),Text(s,4),Text(s,5),Native.sqlite3_column_int64(s,6)!=0?"是":"否" };
                writer.WriteLine(string.Join(",", values.Select(CsvCell)));
            }, Args(date,status,search));
        });
    }
    private static string CsvCell(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    public void DeleteDate(string date) { CheckDate(date); Serialized(() => Execute("DELETE FROM daily_words WHERE date=?",date)); }
    public void DeleteBefore(string date) { CheckDate(date); Serialized(() => Execute("DELETE FROM daily_words WHERE date<?",date)); }
    public void Dispose()
    {
        lock (_submit) { if (_disposed) return; _disposed=true; _queue.CompleteAdding(); }
        _writer.GetAwaiter().GetResult();
        lock (_dbGate) { if (_db != IntPtr.Zero) { Native.sqlite3_close_v2(_db); _db=IntPtr.Zero; } }
        _queue.Dispose();
    }
    private Exception Failure() => new InvalidOperationException("Daily SQLite: " + Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(_db)));
    private static string Text(IntPtr statement,int column) => Marshal.PtrToStringUTF8(Native.sqlite3_column_text(statement,column)) ?? "";
    private void Execute(string sql, params object[] args) => Run(sql,null,args);
    private void Run(string sql, Action<IntPtr>? row, params object[] args)
    {
        IntPtr statement=IntPtr.Zero;
        try
        {
            if (Native.sqlite3_prepare_v2(_db,sql,-1,out statement,IntPtr.Zero)!=0) throw Failure();
            for (int i=0;i<args.Length;i++)
            {
                int rc = args[i] is string text ? Native.sqlite3_bind_text(statement,i+1,text,-1,new IntPtr(-1)) : Native.sqlite3_bind_int64(statement,i+1,Convert.ToInt64(args[i],CultureInfo.InvariantCulture));
                if(rc!=0)throw Failure();
            }
            int code;
            while ((code=Native.sqlite3_step(statement))==100) row?.Invoke(statement);
            if(code!=101)throw Failure();
        }
        catch(Exception ex) { Error(ex); throw; }
        finally { if(statement!=IntPtr.Zero)Native.sqlite3_finalize(statement); }
    }
    private static class Native
    {
        private const string Dll="winsqlite3.dll";
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path,out IntPtr db,int flags,IntPtr vfs);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_busy_timeout(IntPtr db,int ms);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,int length,out IntPtr statement,IntPtr tail);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_bind_text(IntPtr statement,int index,[MarshalAs(UnmanagedType.LPUTF8Str)] string value,int length,IntPtr destructor);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_bind_int64(IntPtr statement,int index,long value);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_step(IntPtr statement);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(IntPtr statement);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_text(IntPtr statement,int index);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern long sqlite3_column_int64(IntPtr statement,int index);
    }
}
