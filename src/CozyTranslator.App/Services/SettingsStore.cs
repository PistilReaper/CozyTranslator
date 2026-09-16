using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CozyTranslator.Core;

namespace CozyTranslator.Desktop.Services;
public enum ThemePreference { System, Light, Dark }
public enum TranslationTrigger { Clipboard, RightClick, Manual }

public sealed record AppSettings
{
    public ProviderSettings Provider {get;init;}=new();
    public string SystemPrompt {get;init;}=QueryPlanner.DefaultPrompt;
    public int Style {get;init;}=2;
    public TranslationDirection Direction {get;init;}=TranslationDirection.Auto;
    public string Hotkey {get;init;}="Ctrl+Alt+T";
    public TranslationTrigger Trigger {get;init;}=TranslationTrigger.Clipboard;
    public ThemePreference Theme {get;init;}=ThemePreference.System;
    public int FontSize {get;init;}=18;
    public double BubbleLeft {get;init;}=double.NaN;
    public double BubbleTop {get;init;}=double.NaN;
    public double PanelLeft {get;init;}=double.NaN;
    public double PanelTop {get;init;}=double.NaN;
    public double PanelWidth {get;init;}=520;
    public double PanelHeight {get;init;}=700;
}
public sealed class SettingsStore(string? folder=null)
{
    private readonly string directory=folder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CozyTranslator");
    private static readonly JsonSerializerOptions options=new(){WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
    public AppSettings Load()
    {
        var path=Path.Combine(directory,"settings.json");if(!File.Exists(path))return new();
        var json=File.ReadAllText(path);
        var settings=JsonSerializer.Deserialize<AppSettings>(json,options)??throw new InvalidDataException("设置文件为空。");
        using var document=JsonDocument.Parse(json);
        if(!document.RootElement.TryGetProperty(nameof(AppSettings.Trigger),out _) &&
            document.RootElement.TryGetProperty("AutoTranslateClipboard",out var previous))
            settings=settings with {Trigger=previous.GetBoolean()?TranslationTrigger.Clipboard:TranslationTrigger.Manual};
        var key=settings.Provider.ApiKey;
        if(key.Length>0)key=Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(key),null,DataProtectionScope.CurrentUser));
        return settings with {Provider=settings.Provider with {ApiKey=key},Style=Math.Clamp(settings.Style,0,4),FontSize=Math.Clamp(settings.FontSize,16,24)};
    }
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        var key=settings.Provider.ApiKey;
        var encrypted=key.Length==0?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key),null,DataProtectionScope.CurrentUser));
        var copy=settings with {Provider=settings.Provider with {ApiKey=encrypted}};
        var path=Path.Combine(directory,"settings.json");var temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(copy,options));File.Move(temp,path,true);
    }
}
