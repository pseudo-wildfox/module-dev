namespace ModuleDev.ActionRuntime;

public sealed class IdempotencyConflictException
    : Exception
{
    public IdempotencyConflictException()
        : base("The same Idempotency-Key was used with a different payload.")
    {
    }
}