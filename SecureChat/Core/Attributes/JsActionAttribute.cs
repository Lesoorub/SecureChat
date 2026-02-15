namespace SecureChat.Core.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class JsActionAttribute : Attribute
{
    public string ActionName { get; }
    public JsActionAttribute(string actionName) => ActionName = actionName;
}
