using System.Reflection;

namespace SecureChat;

internal class TabAttribute : Attribute
{
    public string Address { get; }
    public Type FactoryType { get; }
    public bool InitPage { get; }

    public TabAttribute(string address, Type factory, bool initPage = false)
    {
        Address = address;
        FactoryType = factory;
        InitPage = initPage;
    }

    public static List<(Type type, TabAttribute attr)> GetTypesWithThisAttribute()
    {
        return Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.GetCustomAttribute<TabAttribute>() != null)
            .Select(t => (t, t.GetCustomAttribute<TabAttribute>()!))
            .ToList();
    }
}
