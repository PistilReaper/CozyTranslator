using System.Reflection;
namespace CozyTranslator.Desktop.Services;
public static class VersionInfo
{
    public static string Current => typeof(VersionInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
}
