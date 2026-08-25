namespace ModuleDev.Api.Runtime;

public sealed class ActionExecutionException : Exception
{
    public ActionExecutionException(
        string message)
        : base(message)
    {
    }
}