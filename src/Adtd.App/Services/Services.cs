// IDialogs is adapted from Recast's src/Recast.App/ViewModels/Infrastructure.cs (IShell).
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Adtd.Layout;
using Microsoft.Win32;

namespace Adtd.App.Services;

/// <summary>What the view models need from the desktop: file dialogs and the shell. Faked in tests.</summary>
public interface IDialogs
{
    /// <summary>Asks for a file to open; <paramref name="filter"/> is a Win32 filter string. Null = cancelled.</summary>
    string? AskOpen(string filter, string? initialPath = null);
    /// <summary>Asks where to save; null = cancelled.</summary>
    string? AskSave(string filter, string suggestedPath);
    /// <summary>Opens a file with its default program (a .pptx in PowerPoint).</summary>
    void Open(string path);
    void ShowInFolder(string path);
}

public sealed class WpfDialogs : IDialogs
{
    public string? AskOpen(string filter, string? initialPath = null)
    {
        var d = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (Directory(initialPath) is { } dir) d.InitialDirectory = dir;
        return d.ShowDialog() == true ? d.FileName : null;
    }

    public string? AskSave(string filter, string suggestedPath)
    {
        var d = new SaveFileDialog { Filter = filter, FileName = Path.GetFileName(suggestedPath), OverwritePrompt = true };
        if (Directory(suggestedPath) is { } dir) d.InitialDirectory = dir;
        return d.ShowDialog() == true ? d.FileName : null;
    }

    public void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void ShowInFolder(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"");

    static string? Directory(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var dir = System.IO.Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        return dir != null && System.IO.Directory.Exists(dir) ? dir : null;
    }
}

/// <summary>
/// What the app remembers between runs, in %LOCALAPPDATA%\ADTD-for-PowerPoint\settings.json. Passwords are never
/// part of it.
/// </summary>
public sealed record AppSettings
{
    public string? Server { get; init; }
    public string? User { get; init; }
    public bool ResolveAddresses { get; init; } = true;
    public string? SnapshotPath { get; init; }
    public string? TemplatePath { get; init; }
    public string? LayoutName { get; init; }
    public int LayoutMaster { get; init; }
    public string? IconPath { get; init; }
    public bool KeepExistingSlides { get; init; }
    public DeckOptions Options { get; init; } = new();
    public string? OutputFolder { get; init; }
    public bool OpenWhenDone { get; init; } = true;
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public sealed class SettingsStore(string? path = null) : ISettingsStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ADTD-for-PowerPoint");

    public string FilePath { get; } = path ?? Path.Combine(Folder, "settings.json");

    /// <summary>The saved settings, or the defaults if there are none or they can't be read.</summary>
    public AppSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* settings are a convenience */ }
    }
}
