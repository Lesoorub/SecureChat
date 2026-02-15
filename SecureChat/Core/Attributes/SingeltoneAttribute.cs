namespace SecureChat.Core.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
internal sealed class SingeltoneAttribute : Attribute
{
    public int Order { get; }

    public SingeltoneAttribute(int Order = 0)
    {
        this.Order = Order;
    }
}
