using System.Windows;
namespace CozyTranslator.Desktop.Services;
public static class TypographyService
{
    public static void Apply(int size)
    {
        size=Math.Clamp(size,16,24);
        var resources=Application.Current.Resources;
        resources["BodySize"]=(double)size;
        resources["UiSize"]=(double)size-4;
        resources["SmallSize"]=(double)size-5;
        resources["TitleSize"]=(double)size+4;
        resources["WordSize"]=(double)size+10;
    }
}
