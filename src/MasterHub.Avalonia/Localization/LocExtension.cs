using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.Localization;

public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public string? FallbackValue { get; set; }

    public LocExtension() { }
    public LocExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
            FallbackValue = FallbackValue,
            TargetNullValue = FallbackValue
        };
    }
}
