using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace Adtd.App;

/// <summary>
/// The UI's text, from Strings.resx (English only for now, kept in one place so it can be localised).
/// Code uses <see cref="Get"/> and <see cref="F"/>; XAML uses <c>{l:S Key}</c>.
/// </summary>
public static class Strings
{
    static readonly ResourceManager Resources = new("Adtd.App.Strings", typeof(Strings).Assembly);

    /// <summary>The string called <paramref name="key"/>; the key in brackets if there's no such string, so a gap shows up on screen.</summary>
    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

    /// <summary>The string called <paramref name="key"/> with {0}, {1}… filled in.</summary>
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}

/// <summary><c>{l:S Key}</c> in XAML: the string called Key.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class S(string key) : MarkupExtension
{
    public S() : this("") { }

    [ConstructorArgument("key")]
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
