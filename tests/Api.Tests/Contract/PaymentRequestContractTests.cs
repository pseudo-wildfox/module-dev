using System.Net;
using System.Text.Json;
using Api.Tests.Helpers;

namespace Api.Tests.Contract;

public sealed class PaymentRequestContractTests : ApiTestBase
{
    public PaymentRequestContractTests(
        ApiTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task PaymentRequest_ValidPayload_ReturnsCreated()
    {
        var key = $"test-{Guid.NewGuid():N}";

        using var request =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                new
                {
                    operationKind = "PAYMENT_EXECUTION",
                    amount = "100.00",
                    currency = "RUB"
                },
                TestJwt.CreateCandidate());

        using var response =
            await Client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        using var document =
            await ActionHttpHelper.ReadJsonAsync(response);

        var body =
            document.RootElement;

        Assert.Equal(
            "ok",
            ActionHttpHelper.GetStatus(body));

        Assert.Equal(
            "CREATED",
            ActionHttpHelper.GetOutcome(body));

        var result =
            ActionHttpHelper.GetResult(body);

        Assert.True(
            result.TryGetProperty(
                "operationId",
                out var operationId));

        Assert.True(
            Guid.TryParse(
                operationId.GetString(),
                out _));

        Assert.Equal(
            key,
            ActionHttpHelper.GetResultString(
                body,
                "requestId"));

        Assert.Equal(
            "PAYMENT_EXECUTION",
            ActionHttpHelper.GetResultString(
                body,
                "operationKind"));

        Assert.Equal(
            "100.00",
            ActionHttpHelper.GetResultString(
                body,
                "amount"));

        Assert.Equal(
            "RUB",
            ActionHttpHelper.GetResultString(
                body,
                "currency"));

        Assert.Equal(
            "CREATED",
            ActionHttpHelper.GetResultString(
                body,
                "status"));

        var meta =
            body.GetProperty("meta");

        Assert.True(
            Guid.TryParse(
                meta.GetProperty("correlationId")
                    .GetString(),
                out _));

        Assert.Equal(
            1,
            meta.GetProperty("actionVersion")
                .GetInt32());
    }


    [Fact]
    public async Task PaymentRequest_InvalidAmount_ReturnsPayloadInvalid()
    {
        var key = $"invalid-{Guid.NewGuid():N}";

        using var request =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                new
                {
                    operationKind = "PAYMENT_EXECUTION",
                    amount = "0",
                    currency = "RUB"
                },
                TestJwt.CreateCandidate());

        using var response =
            await Client.SendAsync(request);

        using var document =
            await ActionHttpHelper.ReadJsonAsync(response);

        ActionHttpHelper.AssertError(
            response,
            document.RootElement,
            HttpStatusCode.UnprocessableEntity,
            "payload.invalid");
    }


    [Fact]
    public async Task PaymentRequest_WrongAmountJsonType_ReturnsPayloadInvalid()
    {
        var key = $"invalid-type-{Guid.NewGuid():N}";

        using var request =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                new
                {
                    operationKind = "PAYMENT_EXECUTION",
                    amount = 1,
                    currency = "RUB"
                },
                TestJwt.CreateCandidate());

        using var response =
            await Client.SendAsync(request);

        using var document =
            await ActionHttpHelper.ReadJsonAsync(response);

        ActionHttpHelper.AssertError(
            response,
            document.RootElement,
            HttpStatusCode.UnprocessableEntity,
            "payload.invalid");
    }


    [Fact]
    public async Task PaymentRequest_UnknownProperty_ReturnsPayloadInvalid()
    {
        var key = $"invalid-property-{Guid.NewGuid():N}";

        using var request =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                new
                {
                    operationKind = "PAYMENT_EXECUTION",
                    amount = "100.00",
                    currency = "RUB",
                    status = "COMPLETED"
                },
                TestJwt.CreateCandidate());

        using var response =
            await Client.SendAsync(request);

        using var document =
            await ActionHttpHelper.ReadJsonAsync(response);

        ActionHttpHelper.AssertError(
            response,
            document.RootElement,
            HttpStatusCode.UnprocessableEntity,
            "payload.invalid");
    }


    [Fact]
    public async Task PaymentRequest_MissingIdempotencyKey_ReturnsRequiredError()
    {
        using var request =
            ActionHttpHelper.CreatePaymentRequest(
                null,
                new
                {
                    operationKind = "PAYMENT_EXECUTION",
                    amount = "100.00",
                    currency = "RUB"
                },
                TestJwt.CreateCandidate());

        using var response =
            await Client.SendAsync(request);

        using var document =
            await ActionHttpHelper.ReadJsonAsync(response);

        ActionHttpHelper.AssertError(
            response,
            document.RootElement,
            HttpStatusCode.BadRequest,
            "idempotency.required");
    }


    [Fact]
    public async Task PaymentRequest_WithoutWriteScope_ReturnsAccessDenied()
    {
        var key = $"denied-{Guid.NewGuid():N}";

        using var request =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                new
                {
                    operationKind = "PAYMENT_EXECUTION",
                    amount = "100.00",
                    currency = "RUB"
                },
                TestJwt.CreateDenied());

        using var response =
            await Client.SendAsync(request);

        using var document =
            await ActionHttpHelper.ReadJsonAsync(response);

        ActionHttpHelper.AssertError(
            response,
            document.RootElement,
            HttpStatusCode.Forbidden,
            "access.denied");
    }
}