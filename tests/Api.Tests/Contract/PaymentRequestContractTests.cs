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

    [Fact]
    public async Task PaymentRequest_PersistsOperation()
    {
        var key = $"persist-{Guid.NewGuid():N}";

        using var request =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                new
                {
                    operationKind = "PAYMENT_EXECUTION",
                    amount = "734.21",
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

        var body = document.RootElement;

        var operationId =
            Guid.Parse(
                ActionHttpHelper.GetResultString(
                    body,
                    "operationId"));

        var operations =
            await Autocheck.GetOperationsAsync();

        var operation =
            Assert.Single(
                operations,
                item => item.OperationId == operationId);

        Assert.Equal(
            key,
            operation.RequestId);

        Assert.Equal(
            "PAYMENT_EXECUTION",
            operation.OperationKind);

        Assert.Equal(
            734.21m,
            operation.Amount);

        Assert.Equal(
            "RUB",
            operation.Currency);

        Assert.Equal(
            "CREATED",
            operation.Status);

        Assert.Null(operation.ProcessId);
    }

    [Fact]
    public async Task PaymentRequest_PersistsOperationCreatedEvent()
    {
        var key = $"event-{Guid.NewGuid():N}";

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

        var body = document.RootElement;

        var operationId =
            Guid.Parse(
                ActionHttpHelper.GetResultString(
                    body,
                    "operationId"));

        var events =
            await Autocheck.GetOperationEventsAsync();

        var operationEvent =
            Assert.Single(
                events,
                item => item.OperationId == operationId);

        Assert.Equal(
            "OPERATION_CREATED",
            operationEvent.EventType);

        Assert.False(
            string.IsNullOrWhiteSpace(
                operationEvent.PayloadHash));

        Assert.Matches(
            "^[0-9a-f]{64}$",
            operationEvent.PayloadHash);
    }

    [Fact]
    public async Task PaymentRequest_SameKeyAndPayload_ReplaysOriginalResult()
    {
        var key = $"replay-{Guid.NewGuid():N}";

        var payload = new
        {
            operationKind = "PAYMENT_EXECUTION",
            amount = "250.00",
            currency = "RUB"
        };

        using var firstRequest =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                payload,
                TestJwt.CreateCandidate());

        using var firstResponse =
            await Client.SendAsync(firstRequest);

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

        using var firstDocument =
            await ActionHttpHelper.ReadJsonAsync(firstResponse);

        var firstBody =
            firstDocument.RootElement;

        var firstOperationId =
            ActionHttpHelper.GetResultString(
                firstBody,
                "operationId");

        var operationsAfterFirst =
            await Autocheck.CountOperationsAsync();

        var eventsAfterFirst =
            await Autocheck.CountOperationEventsAsync();

        using var secondRequest =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                payload,
                TestJwt.CreateCandidate());

        using var secondResponse =
            await Client.SendAsync(secondRequest);

        Assert.Equal(
            HttpStatusCode.OK,
            secondResponse.StatusCode);

        using var secondDocument =
            await ActionHttpHelper.ReadJsonAsync(secondResponse);

        var secondBody =
            secondDocument.RootElement;

        Assert.Equal(
            "ok",
            ActionHttpHelper.GetStatus(secondBody));

        Assert.Equal(
            "CREATED",
            ActionHttpHelper.GetOutcome(secondBody));

        var secondOperationId =
            ActionHttpHelper.GetResultString(
                secondBody,
                "operationId");

        Assert.Equal(
            firstOperationId,
            secondOperationId);

        Assert.Equal(
            operationsAfterFirst,
            await Autocheck.CountOperationsAsync());

        Assert.Equal(
            eventsAfterFirst,
            await Autocheck.CountOperationEventsAsync());
    }

    [Fact]
    public async Task PaymentRequest_SameKeyDifferentPayload_ReturnsConflict()
    {
        var key = $"conflict-{Guid.NewGuid():N}";

        var firstPayload = new
        {
            operationKind = "PAYMENT_EXECUTION",
            amount = "100.00",
            currency = "RUB"
        };

        using var firstRequest =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                firstPayload,
                TestJwt.CreateCandidate());

        using var firstResponse =
            await Client.SendAsync(firstRequest);

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

        var operationsAfterFirst =
            await Autocheck.CountOperationsAsync();

        var eventsAfterFirst =
            await Autocheck.CountOperationEventsAsync();

        var secondPayload = new
        {
            operationKind = "PAYMENT_EXECUTION",
            amount = "200.00",
            currency = "RUB"
        };

        using var secondRequest =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                secondPayload,
                TestJwt.CreateCandidate());

        using var secondResponse =
            await Client.SendAsync(secondRequest);

        using var secondDocument =
            await ActionHttpHelper.ReadJsonAsync(secondResponse);

        ActionHttpHelper.AssertError(
            secondResponse,
            secondDocument.RootElement,
            HttpStatusCode.Conflict,
            "idempotency.conflict");

        Assert.Equal(
            operationsAfterFirst,
            await Autocheck.CountOperationsAsync());

        Assert.Equal(
            eventsAfterFirst,
            await Autocheck.CountOperationEventsAsync());
    }

    [Fact]
    public async Task PaymentRequest_ConcurrentIdenticalRequests_CreateOneOperation()
    {
        var key = $"concurrent-{Guid.NewGuid():N}";

        var payload = new
        {
            operationKind = "PAYMENT_EXECUTION",
            amount = "500.00",
            currency = "RUB"
        };

        var responses =
            await ActionHttpHelper.SendConcurrentAsync(
                Client,
                20,
                _ =>
                    ActionHttpHelper.CreatePaymentRequest(
                        key,
                        payload,
                        TestJwt.CreateCandidate()));

        try
        {
            Assert.All(
                responses,
                response => Assert.Equal(
                    HttpStatusCode.OK,
                    response.StatusCode));

            var operationIds = new HashSet<Guid>();

            foreach (var response in responses)
            {
                using var document =
                    await ActionHttpHelper.ReadJsonAsync(response);

                var operationId =
                    Guid.Parse(
                        ActionHttpHelper.GetResultString(
                            document.RootElement,
                            "operationId"));

                operationIds.Add(operationId);
            }

            Assert.Single(operationIds);

            var operations =
                await Autocheck.GetOperationsAsync();

            var operation =
                Assert.Single(
                    operations,
                    item =>
                        operationIds.Contains(
                            item.OperationId));

            Assert.Equal(
                key,
                operation.RequestId);

            var events =
                await Autocheck.GetOperationEventsAsync();

            var createdEvents =
                events
                    .Where(e =>
                        e.OperationId ==
                        operation.OperationId)
                    .ToArray();

            Assert.Single(createdEvents);

            Assert.Equal(
                "OPERATION_CREATED",
                createdEvents[0].EventType);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task PaymentRequest_AfterConcurrentExecution_ReplaysOriginalResult()
    {
        var key = $"concurrent-replay-{Guid.NewGuid():N}";

        var payload = new
        {
            operationKind = "PAYMENT_EXECUTION",
            amount = "600.00",
            currency = "RUB"
        };

        var responses =
            await ActionHttpHelper.SendConcurrentAsync(
                Client,
                20,
                _ =>
                    ActionHttpHelper.CreatePaymentRequest(
                        key,
                        payload,
                        TestJwt.CreateCandidate()));

        string operationId;

        try
        {
            Assert.All(
                responses,
                response => Assert.Equal(
                    HttpStatusCode.OK,
                    response.StatusCode));

            using var firstDocument =
                await ActionHttpHelper.ReadJsonAsync(
                    responses[0]);

            operationId =
                ActionHttpHelper.GetResultString(
                    firstDocument.RootElement,
                    "operationId");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        using var replayRequest =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                payload,
                TestJwt.CreateCandidate());

        using var replayResponse =
            await Client.SendAsync(
                replayRequest);

        Assert.Equal(
            HttpStatusCode.OK,
            replayResponse.StatusCode);

        using var replayDocument =
            await ActionHttpHelper.ReadJsonAsync(
                replayResponse);

        Assert.Equal(
            operationId,
            ActionHttpHelper.GetResultString(
                replayDocument.RootElement,
                "operationId"));

        var operations =
            await Autocheck.GetOperationsAsync();

        Assert.Single(
            operations,
            item =>
                item.OperationId ==
                Guid.Parse(operationId));

        var events =
            await Autocheck.GetOperationEventsAsync();

        Assert.Single(
            events,
            item =>
                item.OperationId ==
                Guid.Parse(operationId) &&
                item.EventType ==
                "OPERATION_CREATED");
    }


    [Fact]
    public async Task PaymentRequest_CompletedKeyWithDifferentPayload_ReturnsConflict()
    {
        var key = $"completed-conflict-{Guid.NewGuid():N}";

        var firstPayload = new
        {
            operationKind = "PAYMENT_EXECUTION",
            amount = "100.00",
            currency = "RUB"
        };

        using (var firstRequest =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                firstPayload,
                TestJwt.CreateCandidate()))
        using (var firstResponse =
            await Client.SendAsync(firstRequest))
        {
            Assert.Equal(
                HttpStatusCode.OK,
                firstResponse.StatusCode);
        }

        var operationsAfterFirst =
            await Autocheck.CountOperationsAsync();

        var eventsAfterFirst =
            await Autocheck.CountOperationEventsAsync();

        var differentPayload = new
        {
            operationKind = "PAYMENT_EXECUTION",
            amount = "999.00",
            currency = "RUB"
        };

        using var conflictRequest =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                differentPayload,
                TestJwt.CreateCandidate());

        using var conflictResponse =
            await Client.SendAsync(
                conflictRequest);

        using var conflictDocument =
            await ActionHttpHelper.ReadJsonAsync(
                conflictResponse);

        ActionHttpHelper.AssertError(
            conflictResponse,
            conflictDocument.RootElement,
            HttpStatusCode.Conflict,
            "idempotency.conflict");

        Assert.Equal(
            operationsAfterFirst,
            await Autocheck.CountOperationsAsync());

        Assert.Equal(
            eventsAfterFirst,
            await Autocheck.CountOperationEventsAsync());
    }


    [Fact]
    public async Task PaymentRequest_SameKeyForDifferentPrincipals_IsIndependent()
    {
        var operationsBefore =
            await Autocheck.CountOperationsAsync();

        var key = $"scope-{Guid.NewGuid():N}";

        var payload = new
        {
            operationKind = "PAYMENT_EXECUTION",
            amount = "300.00",
            currency = "RUB"
        };

        using var firstRequest =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                payload,
                TestJwt.CreateCandidate());

        using var firstResponse =
            await Client.SendAsync(firstRequest);

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

        using var secondRequest =
            ActionHttpHelper.CreatePaymentRequest(
                key,
                payload,
                TestJwt.CreateAnotherCandidate());

        using var secondResponse =
            await Client.SendAsync(secondRequest);

        Assert.Equal(
            HttpStatusCode.OK,
            secondResponse.StatusCode);

        using var firstDocument =
            await ActionHttpHelper.ReadJsonAsync(
                firstResponse);

        using var secondDocument =
            await ActionHttpHelper.ReadJsonAsync(
                secondResponse);

        var firstOperationId =
            ActionHttpHelper.GetResultString(
                firstDocument.RootElement,
                "operationId");

        var secondOperationId =
            ActionHttpHelper.GetResultString(
                secondDocument.RootElement,
                "operationId");

        Assert.NotEqual(
            firstOperationId,
            secondOperationId);

        var operations =
            await Autocheck.GetOperationsAsync();

        Assert.Equal(
            operationsBefore + 2,
            operations.Count);

        Assert.Contains(
            operations,
            operation =>
                operation.OperationId ==
                Guid.Parse(firstOperationId));

        Assert.Contains(
            operations,
            operation =>
                operation.OperationId ==
                Guid.Parse(secondOperationId));
    }
}