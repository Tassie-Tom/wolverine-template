using Wolverine.Http;

namespace Api.Features.Health;

public static class HealthEndpoint
{
    [WolverineGet("/hello")]
    public static string Hello()
    {
        return "Hello from Wolverine Template!";
    }
}
