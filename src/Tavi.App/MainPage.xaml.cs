namespace Tavi.App;

public partial class MainPage : ContentPage
{
    private readonly LocalWebHost _localWebHost;
    private bool _isLoading;

    public MainPage(LocalWebHost localWebHost)
    {
        InitializeComponent();
        _localWebHost = localWebHost;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        await LoadApplicationAsync();
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        await LoadApplicationAsync();
    }

    private async Task LoadApplicationAsync()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        ShowLoading();
        try
        {
            Uri address = await _localWebHost.StartAsync();
            Browser.Source = address.AbsoluteUri;
            Browser.IsVisible = true;
        }
        catch (Exception exception)
        {
            ShowError($"本地服务启动失败。\n\n{exception.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async void OnBrowserNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out Uri? uri)
            || uri.IsLoopback
            || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        e.Cancel = true;
        await Launcher.Default.OpenAsync(uri);
    }

    private void OnBrowserNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result == WebNavigationResult.Success)
        {
            LoadingPanel.IsVisible = false;
            ErrorPanel.IsVisible = false;
            return;
        }

        ShowError($"界面加载失败（{e.Result}）。请重试，或检查本地日志。");
    }

    private void ShowLoading()
    {
        Browser.IsVisible = false;
        ErrorPanel.IsVisible = false;
        LoadingPanel.IsVisible = true;
    }

    private void ShowError(string message)
    {
        Browser.IsVisible = false;
        LoadingPanel.IsVisible = false;
        ErrorMessage.Text = message;
        ErrorPanel.IsVisible = true;
    }
}
