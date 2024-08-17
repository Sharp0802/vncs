using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using vncs.Net;

namespace vncs;

public partial class MainView : UserControl
{
    private string _remoteEndPointText = "127.0.0.1:6974";

    private Node? _node;
    
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

    private async void OnRun(object? sender, RoutedEventArgs e)
    {
        if (_node is null)
            await Run();
        else
            await Abort();
    }

    private async Task Run()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ConfigurationPanel.IsEnabled = false;
            RunButtonIcon.Text           = "||";
            RunButtonIcon.Foreground     = Brushes.Red;
            RunButtonText.Text           = "Abort";
        });

        var root = false;
        await Dispatcher.UIThread.InvokeAsync(() => root = IsRoot);
        
        await Task.Factory.StartNew(() =>
        {
            _node = root ? new Node() : new Node(IPEndPoint.Parse(RemoteEndPointText));
            if (_node.Initialize())
                _node.BeginExecution();
        });
    }

    private async Task Abort()
    {
        await Task.Factory.StartNew(() =>
        {
            _node.EndExecution();
            _node.Dispose();
            _node = null;
            
            Dispatcher.UIThread.Post(() =>
            {
                ConfigurationPanel.IsEnabled = true;
                RunButtonIcon.Text           = "|>";
                RunButtonIcon.Foreground     = Brushes.Green;
                RunButtonText.Text           = "Run";
            });
        });
    }

    private void OnInitialized(object? sender, EventArgs e)
    {
        Logger.View = LogPanel;
        
        Logger.Info($"Virtual Networked Computing System (VNCS) {typeof(Program).Assembly.GetName().Version}");
        Logger.Warn("DISCLAIMER: This is experimental version of VNCS; Functionality of features are not guaranteed");
    }

    [GeneratedRegex(@"[0-9]{1,3}(\.?[0-9]{1,3}){3}(:[0-9]{1,5})?")]
    private static partial Regex EndPointFormatRegex();

    private async void OnUpload(object? sender, RoutedEventArgs e)
    {
        var window = ((IClassicDesktopStyleApplicationLifetime)App.Current.ApplicationLifetime!).MainWindow;
        
        var dialog = new OpenFileDialog
        {
            AllowMultiple = false,
            Filters = [ new FileDialogFilter{ Extensions = [ "dll" ] } ],
            Title = "Select COFF image"
        };
        var file = (await dialog.ShowAsync(window))?.First();
        if (file is null)
            return;

        var bytes = await File.ReadAllBytesAsync(file);
        if (_node is null)
        {
            Logger.Fail("Service not started");
            return;
        }
        
        _node.UploadCode(bytes);
    }
}