using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Security;

namespace SecureChat;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        //byte[] bytes = new byte[64];
        //SecureRandom.Shared.NextBytes(bytes);

        //var T = (Convert.ToBase64String(bytes));

        var services = new ServiceCollection();
        // Регистрация зависимостей
        services.AddSingleton(new HttpClient());
        services.AddSingletonsFromAssembly();
        var serviceProvider = services.BuildServiceProvider();


        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(serviceProvider));
    }
}
