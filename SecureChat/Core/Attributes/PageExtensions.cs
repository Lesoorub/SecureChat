using Microsoft.Extensions.DependencyInjection;

namespace SecureChat.Core.Attributes;

public static class PageExtensions
{
    public static IServiceCollection AddPageControllers(this IServiceCollection services)
    {
        var pageTypes = PageAttribute.GetTypesWithThisAttribute();

        foreach (var (type, _) in pageTypes)
        {
            // Регистрируем сам класс контроллера как Transient (создается заново при навигации)
            services.AddTransient(type);
        }

        return services;
    }
}