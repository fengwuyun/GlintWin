using System.Text.RegularExpressions;
namespace GlintWin;
public static class WordText
{
    public static string CleanTail(string text)
    {
        string value=text.Trim();
        if(!value.Any(c=>c is >= '\u4e00' and <= '\u9fff'))return value;
        while(value.Length>0 && (char.IsPunctuation(value[^1]) || char.IsSymbol(value[^1]) || char.IsWhiteSpace(value[^1])))
        {
            if(Regex.IsMatch(value,@"[A-Za-z][A-Za-z0-9]*(?:\+\+|#)$"))break;
            value=value[..^1];
        }
        int end=value.Length;while(end>0&&char.IsDigit(value[end-1]))end--;
        if(end>0&&end<value.Length&&value[end-1] is >= '\u4e00' and <= '\u9fff')value=value[..end];
        return value;
    }
}
