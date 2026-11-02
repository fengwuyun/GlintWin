using System.Drawing;
namespace GlintWin;
public sealed class PositionStabilizer
{
    private const int HoldMs = 800, ConfirmMinMs = 60, ConfirmMaxMs = 400;
    private Rectangle? _row, _pending;
    private long _lastSeen, _pendingSince;
    public bool NeedsConfirmation => !_row.HasValue || _pending.HasValue;
    public void Reset() { _row = _pending = null; _lastSeen = _pendingSince = 0; }
    public void Observe(Rectangle row, int width, int height, long nowMs)
    {
        if (_row.HasValue && nowMs - _lastSeen > HoldMs) Reset();
        if (row.Left < 0 || row.Top < 0 || row.Right > width || row.Bottom > height || row.Width < 20 || row.Height < 5 || row.Height > height * .65) return;
        int tolerance = Math.Max(3, (_row ?? row).Height / 6);
        if (_row.HasValue && Near(_row.Value, row, tolerance))
        { _pending = null; _lastSeen = nowMs; return; }
        if (_pending.HasValue && Near(_pending.Value, row, tolerance) && nowMs - _pendingSince <= ConfirmMaxMs)
        {
            if (nowMs - _pendingSince < ConfirmMinMs) return;
            _row = row; _pending = null; _lastSeen = nowMs;
        }
        else { _pending = row; _pendingSince = nowMs; }
    }
    private static bool Near(Rectangle a, Rectangle b,int tolerance) => Math.Abs(a.Left-b.Left)<=tolerance && Math.Abs(a.Bottom-b.Bottom)<=tolerance;
    public bool TryGet(NativeMethods.RECT window,long nowMs,out NativeMethods.RECT anchor)
    {
        anchor=default;
        if (!_row.HasValue || nowMs-_lastSeen>HoldMs) return false;
        var r=_row.Value;
        if(r.Bottom>window.Bottom-window.Top || r.Left>=window.Right-window.Left) return false;
        anchor = new NativeMethods.RECT {Left=window.Left+r.Left,Top=window.Top+r.Top,Right=window.Left+r.Right,Bottom=window.Top+r.Bottom};
        return true;
    }
}
