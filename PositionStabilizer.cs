using System.Drawing;
namespace GlintWin;
public sealed class PositionStabilizer
{
    private Rectangle? _row;
    private Rectangle? _pending;
    private long _lastSeen;
    public void Reset() { _row = _pending = null; _lastSeen = 0; }
    public void Observe(Rectangle row, int width, int height, long nowMs)
    {
        if (row.Left < 0 || row.Top < 0 || row.Right > width || row.Bottom > height || row.Width < 20 || row.Height < 5 || row.Height > height * .65) return;
        _lastSeen = nowMs;
        if (_row is null) { _row = row; return; }
        int tolerance = Math.Max(3, _row.Value.Height / 6);
        if (Near(_row.Value, row, tolerance)) { _pending = null; return; }
        if (_pending.HasValue && Near(_pending.Value, row, tolerance)) { _row = row; _pending = null; }
        else _pending = row;
    }
    private static bool Near(Rectangle a, Rectangle b,int tolerance) => Math.Abs(a.Left-b.Left)<=tolerance && Math.Abs(a.Bottom-b.Bottom)<=tolerance;
    public bool TryGet(NativeMethods.RECT window,long nowMs,out NativeMethods.RECT anchor)
    {
        anchor=default;
        if (!_row.HasValue || nowMs-_lastSeen>800) return false;
        var r=_row.Value;
        if(r.Bottom>window.Bottom-window.Top || r.Left>=window.Right-window.Left) return false;
        anchor = new NativeMethods.RECT {Left=window.Left+r.Left,Top=window.Top+r.Top,Right=window.Left+r.Right,Bottom=window.Top+r.Bottom};
        return true;
    }
}
