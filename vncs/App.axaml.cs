using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace vncs;

public partial class App : Application
{
    public override void Initialize()
    {
        Current = this;
        AvaloniaXamlLoader.Load(this);
    }
    
    public new static App Current { get; private set; } = null!;

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow();
                break;
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new MainView();
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}