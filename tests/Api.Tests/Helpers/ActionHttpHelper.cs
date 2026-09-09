using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Api.Tests.Helpers;

public static class ActionHttpHelper
{
    public static HttpRequestMessage CreatePaymentRequest(
        string? idempotencyKey,
        object payload,
        string? accessToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/payment/request");

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add(
                "Idempotency-Key",
                idempotencyKey);
        }

        request.Content = JsonContent.Create(payload);

        return request;
    }

    public static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        var json =
            await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(json);
    }

    public static string GetStatus(
        JsonElement body)
    {
        return body
            .GetProperty("status")
            .GetString()
            ?? throw new InvalidOperationException(
                "Response does not contain string property 'status'.");
    }

    public static string GetCode(
        JsonElement body)
    {
        return body
            .GetProperty("code")
            .GetString()
            ?? throw new InvalidOperationException(
                "Response does not contain string property 'code'.");
    }

    public static string GetOutcome(
        JsonElement body)
    {
        return body
            .GetProperty("outcome")
            .GetString()
            ?? throw new InvalidOperationException(
                "Response does not contain string property 'outcome'.");
    }

    public static JsonElement GetResult(
        JsonElement body)
    {
        return body.GetProperty("result");
    }

    public static string GetResultString(
        JsonElement body,
        string propertyName)
    {
        return GetResult(body)
            .GetProperty(propertyName)
            .GetString()
            ?? throw new InvalidOperationException(
                $"Result property '{propertyName}' is missing or not a string.");
    }

    public static void AssertError(
        HttpResponseMessage response,
        JsonElement body,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Xunit.Assert.Equal(
            expectedStatus,
            response.StatusCode);

        Xunit.Assert.Equal(
            "error",
            GetStatus(body));

        Xunit.Assert.Equal(
            expectedCode,
            GetCode(body));

        Xunit.Assert.True(
            body.TryGetProperty("message", out _));

        Xunit.Assert.True(
            body.TryGetProperty("retryable", out _));

        Xunit.Assert.True(
            body.TryGetProperty("details", out _));

        var meta =
            body.GetProperty("meta");

        Xunit.Assert.True(
            meta.TryGetProperty("correlationId", out var correlationId));

        Xunit.Assert.True(
            Guid.TryParse(
                correlationId.GetString(),
                out _));

        Xunit.Assert.True(
            meta.TryGetProperty("actionVersion", out var actionVersion));

        Xunit.Assert.Equal(
            1,
            actionVersion.GetInt32());
    }

    public static async Task<IReadOnlyList<HttpResponseMessage>> SendConcurrentAsync(
        HttpClient client,
        int count,
        Func<int, HttpRequestMessage> requestFactory)
    {
        var tasks = Enumerable
            .Range(0, count)
            .Select(async index =>
            {
                using var request = requestFactory(index);

                // Response нужно вернуть, а не dispose'ить внутри.
                return await client.SendAsync(request);
            });

        return await Task.WhenAll(tasks);
    }

    public static HttpRequestMessage CreateOperationGetRequest(
        object payload,
        string token)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/operation/get");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        request.Content =
            JsonContent.Create(payload);

        return request;
    }

    public static HttpRequestMessage CreateGenericRequest(
        string path,
        object payload,
        string token,
        string? idempotencyKey = null)
    {
        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                path);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add(
                "Idempotency-Key",
                idempotencyKey);
        }

        request.Content =
            JsonContent.Create(payload);

        return request;
    }
}