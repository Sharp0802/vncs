using System;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace vncs;

public enum Level
{
    Info,
    Warn,
    Fail
}

public partial class MainView : UserControl
{
    private string _remoteEndPointText = "127.0.0.1";
    
    public MainView()
    {
        InitializeComponent();
    }

    public static readonly StyledProperty<bool> IsRootProperty =
        AvaloniaProperty.Register<MainWindow, bool>(nameof(IsRoot));

    public static readonly StyledProperty<string> LocalEndPointTextProperty =
        AvaloniaProperty.Register<MainWindow, string>(nameof(LocalEndPointText));

    public bool IsRoot
    {
        get => GetValue(IsRootProperty);
        set => SetValue(IsRootProperty, value);
    }

    public string RemoteEndPointText
    {
        get => _remoteEndPointText;
        set
        {
            var match = EndPointFormatRegex().Match(value);
            if (!match.Success || match.Value != value)
                throw new DataValidationException("Invalid IP format");
            _remoteEndPointText = value;
        }
    }

    public string LocalEndPointText
    {
        get => GetValue(LocalEndPointTextProperty);
        set => SetValue(LocalEndPointTextProperty, value);
    }

    private void Log(Level level, string message)
    {
        LogPanel.Children.Add(new LogBlob
        {
            Icon = (Geometry)App.Current.Resources[level switch
            {
                Level.Info => "Info",
                Level.Warn => "Warn",
                Level.Fail => "Fail"
            }]!,
            Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
            Background = new SolidColorBrush(Color.FromRgb(
                level is Level.Warn or Level.Fail ? (byte)0x44 : (byte)0x33,
                level is Level.Warn ? (byte)0x44 : (byte)0x33, 
                0x33))
        });
    }

    private void OnRun(object? sender, RoutedEventArgs e)
    {
        ConfigurationPanel.IsEnabled = false;
    }

    private void OnInitialized(object? sender, EventArgs e)
    {
        Log(Level.Info, $"Virtual Networked Computing System (VNCS) {typeof(Program).Assembly.GetName().Version}");
        Log(Level.Warn, "DISCLAIMER: This is experimental version of VNCS; Functionality of features are not guaranteed");
    }

    [GeneratedRegex(@"[0-9]{1,3}(\.?[0-9]{1,3}){3}")]
    private static partial Regex EndPointFormatRegex();
}