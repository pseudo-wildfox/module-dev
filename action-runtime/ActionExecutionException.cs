namespace ModuleDev.ActionRuntime;

public sealed class ActionExecutionException : Exception
{
    public ActionExecutionException(
        string message)
        : base(message)
    {
    }
}