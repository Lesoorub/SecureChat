using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SecureChat.Core.Attributes;

namespace SecureChat.Infrastructure.DI;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует все классы в текущей сборке с атрибутом SingeltoneAttribute как синглтоны
    /// </summary>
    public static IServiceCollection AddSingletonsFromAssembly(
        this IServiceCollection services,
        Assembly? assembly = null)
    {
        assembly ??= Assembly.GetExecutingAssembly();

        // Находим все классы с атрибутом SingeltoneAttribute
        var singletonTypes = assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<SingeltoneAttribute>() != null)
            .OrderBy(type => type.GetCustomAttribute<SingeltoneAttribute>()?.Order ?? 0)
            .ToList();

        foreach (var implementationType in singletonTypes)
        {
            RegisterSingleton(services, implementationType);
        }

        return services;
    }

    private static void RegisterSingleton(IServiceCollection services, Type implementationType)
    {
        if (!implementationType.IsClass || implementationType.IsAbstract)
            return;

        // Проверяем, реализует ли класс интерфейсы
        var interfaces = implementationType.GetInterfaces();

        if (interfaces.Length > 0)
        {
            // Регистрируем для каждого интерфейса
            foreach (var interfaceType in interfaces)
            {
                services.AddSingleton(interfaceType, implementationType);
            }
        }
        else
        {
            // Регистрируем сам класс
            services.AddSingleton(implementationType);
        }
    }
}