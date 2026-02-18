using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using SecureChat.Core.Attributes;
using SecureChat.Infrastructure.WebView;
using SecureChat.UI.Base;

namespace SecureChat;

public partial class MainForm : Form
{
    private AbstractPage? _currentPage;

    private string? _initPageAddress;
    private readonly Dictionary<string, Type> _routeMap = new();
    private readonly ServiceProvider _serviceProvider;

    public MainForm(ServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        InitializeComponent();
        _webView.DefaultBackgroundColor = Color.Transparent;
        if (this.DesignMode)
        {
            return;
        }

        foreach (var (type, attr) in PageAttribute.GetTypesWithThisAttribute())
        {
            if (attr.InitPage)
            {
                _initPageAddress = attr.Address;
            }
            _routeMap[attr.Address] = type;
        }
        _ = InitializeAsync(); // Запуск инициализации
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

        // Отключает навигацию через жесты и спец. кнопки мыши
        _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
        _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        _webView.CoreWebView2.Settings.IsSwipeNavigationEnabled = false;

        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        _webView.CoreWebView2.Navigate("https://app.localhost" + _initPageAddress);
    }

    private void _webView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
        {
            return;
        }

        _currentPage?.PageLoaded();
    }

    private void _webView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.NavigationKind != CoreWebView2NavigationKind.NewDocument)
        {
            e.Cancel = true;
            return;
        }

        (_currentPage as IDisposable)?.Dispose();

        var uri = new Uri(e.Uri);
        if (_routeMap.TryGetValue(uri.AbsolutePath, out var pageType))
        {
            _currentPage = (AbstractPage)ActivatorUtilities.CreateInstance(
                _serviceProvider,
                pageType,
                new WebViewWrapper(_webView, _openFileDialog, _saveFileDialog)
            );
        }
        else
        {
            _currentPage = null;
        }
    }

    private void _webView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        _currentPage?.ProcessPostMessage(e.WebMessageAsJson);
    }
}
