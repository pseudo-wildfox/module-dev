namespace Api.Tests.Helpers;

public static class TestActionName
{
    public static string CreateModule()
    {
        return
            $"regtest{Guid.NewGuid():N}"[..20];
    }
}