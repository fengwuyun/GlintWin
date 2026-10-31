namespace GlintWin;

public sealed record OcrWordData(string Text, double X, double Y, double Width, double Height);
public sealed record OcrLineData(string Text, IReadOnlyList<OcrWordData> Words);
