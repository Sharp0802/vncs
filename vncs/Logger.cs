using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace vncs;

public enum Level
{
    Info,
    Warn,
    Fail
}

public static class Logger
{
    public static StackPanel? View { get; set; }

    public static void Log(Level level, string message)
    {
        if (View is null)
            throw new NullReferenceException();
        
        View.Children.Add(new LogBlob
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
    
    public static void Info(string msg)
    {
        Log(Level.Info, msg);
    }

    public static void Warn(string msg)
    {
        Log(Level.Warn, msg);
    }

    public static void Fail(string msg)
    {
        Log(Level.Fail, msg);
    }
}