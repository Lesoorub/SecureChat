using System.Reflection;

namespace SecureChat.Core.Attributes;

internal class PageAttribute : Attribute
{
    public string Address { get; }
    public bool InitPage { get; }

    public PageAttribute(string address, bool initPage = false)
    {
        Address = address;
        InitPage = initPage;
    }

    public static List<(Type type, PageAttribute attr)> GetTypesWithThisAttribute()
    {
        return Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.GetCustomAttribute<PageAttribute>() != null)
            .Select(t => (t, t.GetCustomAttribute<PageAttribute>()!))
            .ToList();
    }
}
