using System.Net;
using Api.Tests.Helpers;

namespace Api.Tests.Contract;

public sealed class OperationGetContractTests : ApiTestBase
{
    public OperationGetContractTests(
        ApiTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task OperationGet_ExistingOperation_ReturnsSameOperation()
    {
        var requestId =
            $"operation-get-{Guid.NewGuid():N}";

        var createPayload = new
        {
            operationKind = "PAYMENT_EXECUTION",
            amount = "734.21",
            currency = "RUB"
        };

        using var createRequest =
            ActionHttpHelper.CreatePaymentRequest(
                requestId,
                createPayload,
                TestJwt.CreateCandidate());

        using var createResponse =
            await Client.SendAsync(createRequest);

        Assert.Equal(
            HttpStatusCode.OK,
            createResponse.StatusCode);

        using var createDocument =
            await ActionHttpHelper.ReadJsonAsync(
                createResponse);

        var createdBody =
            createDocument.RootElement;

        var operationId =
            Guid.Parse(
                ActionHttpHelper.GetResultString(
                    createdBody,
                    "operationId"));

        using var getRequest =
            ActionHttpHelper.CreateOperationGetRequest(
                new
                {
                    operationId
                },
                TestJwt.CreateReader());

        using var getResponse =
            await Client.SendAsync(getRequest);

        Assert.Equal(
            HttpStatusCode.OK,
            getResponse.StatusCode);

        using var getDocument =
            await ActionHttpHelper.ReadJsonAsync(
                getResponse);

        var getBody =
            getDocument.RootElement;

        Assert.Equal(
            "ok",
            ActionHttpHelper.GetStatus(getBody));

        Assert.Equal(
            "FOUND",
            ActionHttpHelper.GetOutcome(getBody));

        var result =
            ActionHttpHelper.GetResult(getBody);

        Assert.Equal(
            operationId,
            Guid.Parse(
                result.GetProperty("operationId")
                    .GetString()
                ?? throw new InvalidOperationException(
                    "operationId is missing.")));

        Assert.Equal(
            requestId,
            result.GetProperty("requestId")
                .GetString());

        Assert.Equal(
            "PAYMENT_EXECUTION",
            result.GetProperty("operationKind")
                .GetString());

        Assert.Equal(
            "734.21",
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

    [Fact]
    public async Task OperationGet_UnknownOperation_ReturnsNotFound()
    {
        var unknownOperationId =
            Guid.NewGuid();

        using var request =
            ActionHttpHelper.CreateOperationGetRequest(
                new
                {
                    operationId = unknownOperationId
                },
                TestJwt.CreateReader());

        using var response =
            await Client.SendAsync(request);

        using var document =
            await ActionHttpHelper.ReadJsonAsync(
                response);

        ActionHttpHelper.AssertError(
            response,
            document.RootElement,
            HttpStatusCode.NotFound,
            "operation.not_found");

        var details =
            document.RootElement
                .GetProperty("details");

        Assert.True(
            details.TryGetProperty(
                "operationId",
                out var operationId));

        Assert.Equal(
            unknownOperationId.ToString(),
            operationId.GetString());
    }

    [Fact]
    public async Task OperationGet_InvalidOperationId_ReturnsPayloadInvalid()
    {
        using var request =
            ActionHttpHelper.CreateOperationGetRequest(
                new
                {
                    operationId = "not-a-uuid"
                },
                TestJwt.CreateReader());

        using var response =
            await Client.SendAsync(request);

        using var document =
            await ActionHttpHelper.ReadJsonAsync(
                response);

        ActionHttpHelper.AssertError(
            response,
            document.RootElement,
            HttpStatusCode.UnprocessableEntity,
            "payload.invalid");
    }

    [Fact]
    public async Task OperationGet_WithoutReadScope_ReturnsAccessDenied()
    {
        var operationId =
            Guid.NewGuid();

        using var request =
            ActionHttpHelper.CreateOperationGetRequest(
                new
                {
                    operationId
                },
                TestJwt.CreateDenied());

        using var response =
            await Client.SendAsync(request);

        using var document =
            await ActionHttpHelper.ReadJsonAsync(
                response);

        ActionHttpHelper.AssertError(
            response,
            document.RootElement,
            HttpStatusCode.Forbidden,
            "access.denied");
    }
}