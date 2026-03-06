using Aco228.Common.Infrastructure;
using Aco228.Common.LocalStorage;
using SkiaSharp;

namespace Aco228.ContextualImage.Infrastructure;

public static class FontManager
{
    private static ManagedList<FileInfo> _fontRegular = new();
    private static ManagedList<FileInfo> _fontsBold = new();
    
    public static void LoadFonts(IStorageFolder location)
    {
        var directory = location.GetDirectoryInfo();
        foreach (var insideDir in directory.GetDirectories())
        {
            var insideFolders = insideDir.GetDirectories();
            var staticDir = insideFolders.FirstOrDefault(x => x.Name.Equals("static"));
            if (staticDir == null)
                continue;
            
            var fontBold = staticDir.GetFiles("*.ttf").FirstOrDefault(x => x.Name.Contains("-Bold"));
            if(fontBold != null)
                _fontsBold.AddRange(fontBold);
            
            var fontRegular = staticDir.GetFiles("*.ttf").FirstOrDefault(x => x.Name.Contains("-Regular"));
            if(fontRegular != null)
                _fontRegular.Add(fontRegular);
            
        }

        _fontRegular.ShuffleAgain();
        _fontsBold.ShuffleAgain();
    }

    public static SKTypeface TakeNext(bool isBold = false)
    {
        var fontPath = Path.Combine((isBold ? _fontsBold : _fontRegular).Take()!.FullName);
        if (File.Exists(fontPath))
            return SKTypeface.FromFile(fontPath);

        return SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
    }
}