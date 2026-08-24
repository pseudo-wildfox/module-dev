using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

var app = builder.Build();

app.MapGet(
    "/",
    () => Results.Ok(new { status = "ok" }));

app.MapPost(
    "/api/{module}/{action}",
    async (
        string module,
        string action,
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        CancellationToken cancellationToken) =>
    {
        var apiBaseUrl =
            configuration["API_BASE_URL"]
            ?? "http://api:8080";

        var client =
            httpClientFactory.CreateClient();

        var targetUrl =
            $"{apiBaseUrl}/api/{Uri.EscapeDataString(module)}/{Uri.EscapeDataString(action)}";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                targetUrl);

        if (context.Request.Headers.TryGetValue(
                "Authorization",
                out var authorization))
        {
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                authorization.ToString());
        }

        if (context.Request.Headers.TryGetValue(
                "X-Action-Version",
                out var version))
        {
            request.Headers.TryAddWithoutValidation(
                "X-Action-Version",
                version.ToString());
        }

        if (context.Request.Headers.TryGetValue(
                "Idempotency-Key",
                out var idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                idempotencyKey.ToString());
        }

        request.Content = new StreamContent(
            context.Request.Body);

        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/json");

        using var response =
            await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        context.Response.StatusCode =
            (int)response.StatusCode;

        if (response.Content.Headers.ContentType is not null)
        {
            context.Response.ContentType =
                response.Content.Headers.ContentType.ToString();
        }

        await response.Content.CopyToAsync(
            context.Response.Body,
            cancellationToken);
    });

app.Run();