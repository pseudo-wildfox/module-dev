using System.Text.Json;

namespace ModuleDev.ActionRuntime;

public sealed class ActionDomainException : Exception
{
    public ActionDomainException(
        string code,
        string message,
        bool retryable,
        JsonElement details)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
        Details = details.Clone();
    }

    public string Code { get; }

    public bool Retryable { get; }

    public JsonElement Details { get; }
}