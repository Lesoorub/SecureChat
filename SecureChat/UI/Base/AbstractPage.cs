using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecureChat.Core.Attributes;

namespace SecureChat.UI.Base;

internal abstract class AbstractPage
{
    // Теперь ключ — это ActionName, значение — кортеж (Метод, ТипПараметра, ЭкземплярОбъекта)
    private readonly Dictionary<string, (MethodInfo Method, Type? ParamType, object Target)> _jsActions = new();
    public ILogger Logger { get; }

    protected AbstractPage(ILogger logger)
    {
        Logger = logger;
    }

    protected void InitializeActions()
    {
        _jsActions.Clear();

        // 1. Регистрируем методы самого контроллера (this)
        RegisterFromObject(this);

        // 2. Ищем свойства и поля с атрибутом [SubHandler]
        var type = GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var subHandlerObjects = type.GetMembers(flags)
            .Where(m => m.GetCustomAttribute<SubHandlerAttribute>() != null)
            .Select(m => m switch
            {
                PropertyInfo p => p.GetValue(this),
                FieldInfo f => f.GetValue(this),
                _ => null
            })
            .Where(obj => obj != null);

        foreach (var obj in subHandlerObjects)
        {
            RegisterFromObject(obj!);
        }
    }

    private void RegisterFromObject(object target)
    {
        var methods = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.GetCustomAttribute<JsActionAttribute>() != null);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<JsActionAttribute>()!;
            var param = method.GetParameters().FirstOrDefault();

            _jsActions[attr.ActionName] = (method, param?.ParameterType, target);
        }
    }


    public void ProcessPostMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("action", out var actionProp)) return;

            var action = actionProp.GetString();
            if (action != null && _jsActions.TryGetValue(action, out var handler))
            {
                object?[]? args = null;
                if (handler.ParamType != null)
                {
                    args = [JsonSerializer.Deserialize(json, handler.ParamType)];
                }

                handler.Method.Invoke(handler.Target, args);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Ошибка вызова JS Action: {Action}", json);
        }
    }

    public abstract void PageLoaded();
}
