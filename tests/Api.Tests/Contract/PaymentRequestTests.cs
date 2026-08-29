using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Api.Tests.Contract;

public sealed class PaymentRequestTests
    : ApiTestBase
{
    public PaymentRequestTests(
        PostgreSqlFixture postgres)
        : base(postgres)
    {
    }

    [Fact]
    public async Task PaymentRequest_ReturnsCreated()
    {
        SetBearerToken(
            TestJwt.CreateCandidate());

        var key =
            $"test-{Guid.NewGuid():N}";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/payment/request");

        request.Headers.Add(
            "Idempotency-Key",
            key);

        request.Content =
            JsonContent.Create(
                new
                {
                    operationKind =
                        "PAYMENT_EXECUTION",

                    amount =
                        "100.00",

                    currency =
                        "RUB"
                });

        var response =
            await Client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            "ok",
            body.GetProperty("status")
                .GetString());

        Assert.Equal(
            "CREATED",
            body.GetProperty("outcome")
                .GetString());

        var result =
            body.GetProperty("result");

        Assert.True(
            Guid.TryParse(
                result.GetProperty("operationId")
                    .GetString(),
                out _));

        Assert.Equal(
            key,
            result.GetProperty("requestId")
                .GetString());

        Assert.Equal(
            "100.00",
            result.GetProperty("amount")
                .GetString());

        Assert.Equal(
            "RUB",
            result.GetProperty("currency")
                .GetString());

        Assert.Equal(
            "CREATED",
            result.GetProperty("status")
                .GetString());
    }
}