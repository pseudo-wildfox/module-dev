namespace ModuleDev.ActionRuntime;

public sealed class ActionTimeoutException : Exception
{
    public ActionTimeoutException()
        : base("Action execution timed out.")
    {
    }
}