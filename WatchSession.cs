namespace GlintWin;
// Updated by the producer under its lock, so a dropped hide event cannot erase a session boundary.
public sealed class WatchSession
{
    private bool _visible, _source;
    public long Generation { get; private set; }
    public void Observe(bool visible, bool source)
    {
        if (visible != _visible || (visible && source != _source)) Generation++;
        _visible = visible; _source = source;
    }
}
