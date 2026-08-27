namespace ModuleDev.Api.Runtime;

public sealed class ActionTimeoutException : Exception
{
    public ActionTimeoutException()
        : base("Action execution timed out.")
    {
    }
}