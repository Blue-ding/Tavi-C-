namespace Tavi.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly MainPage _mainPage;
    private readonly LocalWebHost _localWebHost;

    public App(MainPage mainPage, LocalWebHost localWebHost)
    {
        InitializeComponent();
        _mainPage = mainPage;
        _localWebHost = localWebHost;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Window window = new(_mainPage)
        {
            Title = "Tavi",
            Width = 1440,
            Height = 900,
            MinimumWidth = 1024,
            MinimumHeight = 640,
        };
        window.Destroying += OnWindowDestroying;
        return window;
    }

    private async void OnWindowDestroying(object? sender, EventArgs e)
    {
        await _localWebHost.StopAsync();
    }
}
