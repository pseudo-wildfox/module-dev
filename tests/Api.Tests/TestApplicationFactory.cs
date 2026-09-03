using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api.Tests;

public sealed class TestApplicationFactory
    : WebApplicationFactory<Program>
{
    private const string JwtIssuer =
        "moduledev-course-test";

    private const string JwtAudience =
        "moduledev-api-test";

    private const string JwtSigningKey =
        "this-is-a-test-signing-key-at-least-32-bytes-long";

    private readonly string _previousIssuer;
    private readonly string _previousAudience;
    private readonly string _previousSigningKey;

    public TestApplicationFactory(
        PostgreSqlFixture postgres)
    {
        _previousIssuer =
            Environment.GetEnvironmentVariable(
                "COURSE_JWT_ISSUER");

        _previousAudience =
            Environment.GetEnvironmentVariable(
                "COURSE_JWT_AUDIENCE");

        _previousSigningKey =
            Environment.GetEnvironmentVariable(
                "COURSE_JWT_SIGNING_KEY");

        Environment.SetEnvironmentVariable(
            "COURSE_JWT_ISSUER",
            JwtIssuer);

        Environment.SetEnvironmentVariable(
            "COURSE_JWT_AUDIENCE",
            JwtAudience);

        Environment.SetEnvironmentVariable(
            "COURSE_JWT_SIGNING_KEY",
            JwtSigningKey);

        Environment.SetEnvironmentVariable(
            "DB_HOST",
            postgres.Host);

        Environment.SetEnvironmentVariable(
            "DB_PORT",
            postgres.Port.ToString());

        Environment.SetEnvironmentVariable(
            "DB_NAME",
            postgres.Database);

        Environment.SetEnvironmentVariable(
            "DB_USER",
            postgres.Username);

        Environment.SetEnvironmentVariable(
            "DB_PASSWORD",
            postgres.Password);

        Environment.SetEnvironmentVariable(
            "DB_PUBLISHER_USER",
            "course_publisher");

        Environment.SetEnvironmentVariable(
            "DB_PUBLISHER_PASSWORD",
            "test-publisher-password");
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            Environment.SetEnvironmentVariable(
                "COURSE_JWT_ISSUER",
                _previousIssuer);

            Environment.SetEnvironmentVariable(
                "COURSE_JWT_AUDIENCE",
                _previousAudience);

            Environment.SetEnvironmentVariable(
                "COURSE_JWT_SIGNING_KEY",
                _previousSigningKey);
        }

        base.Dispose(disposing);
    }
}