using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureChat.Core.Attributes;
using SecureChat.Infrastructure.DI;

namespace SecureChat;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        // Регистрация зависимостей
        services.AddSingleton(new HttpClient());
        services.AddSingletonsFromAssembly();
        services.AddPageControllers();
        var serviceProvider = services.BuildServiceProvider();


        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(serviceProvider));
    }
}
