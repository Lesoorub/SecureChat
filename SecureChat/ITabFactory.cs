using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.WinForms;

namespace SecureChat;

internal interface ITabFactory
{
    AbstractTab Create(WebView2 webView, ServiceProvider serviceProvider);
}
