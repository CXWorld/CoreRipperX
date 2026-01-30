using System.Windows;

namespace CoreRipperX.UI.Helpers;

/// <summary>
/// A proxy object that allows binding to properties outside of the visual tree,
/// such as DataGridColumn.Visibility.
/// </summary>
public class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

    public object Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
