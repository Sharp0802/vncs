using System;
using System.Collections.Generic;
using Avalonia.Controls;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace vncs
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            /*
            const int port = 65530;

            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                Blocking            = false,
                ExclusiveAddressUse = false,
                NoDelay             = true
            };
            listener.Bind(new IPEndPoint(0, port));
            listener.Listen();

            var sockets = new List<Socket>();
            while (true)
            {
                foreach (var socket in sockets)
                {
                    socket.Poll(0, SelectMode.SelectRead);
                    if (socket == listener)
                    {
                        var accepted = await listener.AcceptAsync();
                        Console.WriteLine($"{accepted.RemoteEndPoint} connected");
                        sockets.Add(accepted);
                    }
                    else
                    {
                        socket.ReceiveAsync();
                    }
                }
            }*/

        }

        private string _remoteEndPointText = "127.0.0.1";

        public static readonly StyledProperty<bool> IsRootProperty =
            AvaloniaProperty.Register<MainWindow, bool>(nameof(IsRoot));
        
        public static readonly StyledProperty<string> LocalEndPointTextProperty =
            AvaloniaProperty.Register<MainWindow, string>(nameof(LocalEndPointText));
        
        public static readonly StyledProperty<string> TerminalTextProperty =
            AvaloniaProperty.Register<MainWindow, string>(nameof(TerminalText));

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

        public string TerminalText 
        { 
            get => GetValue(TerminalTextProperty); 
            set => SetValue(TerminalTextProperty, value);
        }

        private void Log(string message)
        {
            TerminalText += $"[{DateTime.Now:s}] {message}";
        }
        
        private void OnRun(object? sender, RoutedEventArgs e)
        {
            ConfigurationPanel.IsEnabled = false;
            
            
        }

        private void OnInitialized(object? sender, EventArgs e)
        {
            Log($"Virtual Networked Computing System (VNCS) {typeof(Program).Assembly.GetName().Version}");
        }

        [GeneratedRegex(@"[0-9]{1,3}(\.?[0-9]{1,3}){3}")]
        private static partial Regex EndPointFormatRegex();
    }
}