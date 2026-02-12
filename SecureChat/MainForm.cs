using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using NAudio.Wave;

namespace SecureChat;

public partial class MainForm : Form
{
    private AbstractTab? _currentTab;

    private readonly Dictionary<string, ITabFactory> _tabFactories = new();
    private Task _asyncLoadTask;
    private readonly ServiceProvider _serviceProvider;

    [DllImport("user32.dll")]
    static extern bool FlashWindow(IntPtr hwnd, bool bInvert);

    public MainForm(ServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        InitializeComponent();
        _asyncLoadTask = Task.Run(AsyncLoad);
        _ = InitializeAsync(); // Запуск инициализации
    }

    void AsyncLoad()
    {
        try
        {
            foreach (var (type, attr) in TabAttribute.GetTypesWithThisAttribute())
            {
                var factory = (ITabFactory?)Activator.CreateInstance(attr.FactoryType);
                if (factory is null)
                {
                    MessageBox.Show($"Не удалось загрузить страницу: '{attr.Address}'");
                    Environment.Exit(0);
                    return;
                }
                _tabFactories[attr.Address] = factory;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString());
        }
    }

    public void Flash()
    {
        FlashWindow(this.Handle, true);
    }

    async Task InitializeAsync()
    {
        // 1. Ждем создания среды (CoreWebView2)
        await _webView.EnsureCoreWebView2Async();

        _webView.CoreWebView2.Profile.PreferredColorScheme =
            CoreWebView2PreferredColorScheme.Dark;

        _webView.WebMessageReceived += _webView_WebMessageReceived;

        // 2. Маппим домен app.localhost на папку wwwroot
        // "wwwroot" будет искаться относительно .exe файла

        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            hostName: "app.localhost",
            folderPath: "wwwroot/",
            accessKind: CoreWebView2HostResourceAccessKind.Allow
        );

        _webView.NavigationStarting += _webView_NavigationStarting;
        _webView.NavigationCompleted += _webView_NavigationCompleted;

        _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
        _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;

        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        await _asyncLoadTask;
        _webView.CoreWebView2.Navigate("https://app.localhost/chat/index.html");
    }

    private void _webView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _currentTab?.PageLoaded();
    }

    private void _webView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        (_currentTab as IDisposable)?.Dispose();

        var uri = new Uri(e.Uri);

        if (_tabFactories.TryGetValue(uri.AbsolutePath, out var factory))
        {
            _currentTab = factory.Create(_webView, _serviceProvider);
        }
        else
        {
            _currentTab = null;
        }
    }

    private void _webView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        _currentTab?.ProcessPostMessage(e.WebMessageAsJson);
    }
}
