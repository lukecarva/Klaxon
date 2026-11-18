using System.Windows.Markup;
using Klaxon.Core.Localization;

namespace Klaxon.App.Localization;

/// <summary>XAML markup extension that localizes text: <c>{loc:Loc Editor_Name}</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    /// <summary>Localization key to look up.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Returns the localized string for <see cref="Key"/>.</summary>
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
