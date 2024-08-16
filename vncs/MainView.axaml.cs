using System;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace vncs;

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

    private void OnRun(object? sender, RoutedEventArgs e)
    {
        ConfigurationPanel.IsEnabled = false;
    }

    private void OnInitialized(object? sender, EventArgs e)
    {
        Logger.View = LogPanel;
        
        Logger.Info($"Virtual Networked Computing System (VNCS) {typeof(Program).Assembly.GetName().Version}");
        Logger.Warn("DISCLAIMER: This is experimental version of VNCS; Functionality of features are not guaranteed");
    }

    [GeneratedRegex(@"[0-9]{1,3}(\.?[0-9]{1,3}){3}")]
    private static partial Regex EndPointFormatRegex();
}