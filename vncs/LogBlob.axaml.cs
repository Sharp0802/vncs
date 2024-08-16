using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace vncs;

public partial class LogBlob : UserControl
{
    public static readonly StyledProperty<string>   TextProperty = AvaloniaProperty.Register<LogBlob, string>("Text");
    public static readonly StyledProperty<Geometry> IconProperty = AvaloniaProperty.Register<LogBlob, Geometry>("Icon");

    public LogBlob()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Geometry Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}