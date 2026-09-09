using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Api.Tests.Helpers;
using ModuleDev.Api.Security;

namespace Api.Tests.Contract;

public sealed class JwtClaimAlgorithmValidationTests : ApiTestBase
{
    public JwtClaimAlgorithmValidationTests(ApiTestFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task JwtClaimAlgorithmValidation_JwtWithoutIat_IsRejected()
    {
        var key = $"test-{Guid.NewGuid():N}";

        using var request = ActionHttpHelper.CreatePaymentRequest(
            key,
            new
            {
                operationKind = "PAYMENT_EXECUTION",
                amount = "100.00",
                currency = "RUB",
            },
            TestJwt.CreateCandidateWithoutIat()
        );

        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var document = await ActionHttpHelper.ReadJsonAsync(response);

        var body = document.RootElement;

        Assert.Equal("error", ActionHttpHelper.GetStatus(body));
        Assert.Equal("auth.invalid", ActionHttpHelper.GetCode(body));
    }

    [Fact]
    public async Task JwtClaimAlgorithmValidation_JwtWithNumericSub_IsRejected()
    {
        var key = $"test-{Guid.NewGuid():N}";

        using var request = ActionHttpHelper.CreatePaymentRequest(
            key,
            new
            {
                operationKind = "PAYMENT_EXECUTION",
                amount = "100.00",
                currency = "RUB",
            },
            TestJwt.CreateCandidateWithNumericSub()
        );

        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var document = await ActionHttpHelper.ReadJsonAsync(response);

        var body = document.RootElement;

        Assert.Equal("error", ActionHttpHelper.GetStatus(body));
        Assert.Equal("auth.invalid", ActionHttpHelper.GetCode(body));
    }

    [Fact]
    public async Task JwtClaimAlgorithmValidation_JwtWithNumericConsumer_IsRejected()
    {
        var key = $"test-{Guid.NewGuid():N}";

        using var request = ActionHttpHelper.CreatePaymentRequest(
            key,
            new
            {
                operationKind = "PAYMENT_EXECUTION",
                amount = "100.00",
                currency = "RUB",
            },
            TestJwt.CreateCandidateWithNumericConsumer()
        );

        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var document = await ActionHttpHelper.ReadJsonAsync(response);

        var body = document.RootElement;

        Assert.Equal("error", ActionHttpHelper.GetStatus(body));
        Assert.Equal("auth.invalid", ActionHttpHelper.GetCode(body));
    }

    [Fact]
    public async Task JwtClaimAlgorithmValidation_JwtWithBooleanScope_IsRejected()
    {
        var key = $"test-{Guid.NewGuid():N}";

        using var request = ActionHttpHelper.CreatePaymentRequest(
            key,
            new
            {
                operationKind = "PAYMENT_EXECUTION",
                amount = "100.00",
                currency = "RUB",
            },
            TestJwt.CreateCandidateWithBooleanScope()
        );

        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var document = await ActionHttpHelper.ReadJsonAsync(response);

        var body = document.RootElement;

        Assert.Equal("error", ActionHttpHelper.GetStatus(body));
        Assert.Equal("auth.invalid", ActionHttpHelper.GetCode(body));
    }

    [Fact]
    public async Task JwtClaimAlgorithmValidation_JwtWithHs384_IsRejected()
    {
        var key = $"test-{Guid.NewGuid():N}";

        using var request = ActionHttpHelper.CreatePaymentRequest(
            key,
            new
            {
                operationKind = "PAYMENT_EXECUTION",
                amount = "100.00",
                currency = "RUB",
            },
            TestJwt.CreateCandidateWithHs384()
        );

        using var response = await Client.SendAsync(request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var document = await ActionHttpHelper.ReadJsonAsync(response);

        var body = document.RootElement;
        Assert.Equal("error", ActionHttpHelper.GetStatus(body));
        Assert.Equal("auth.invalid", ActionHttpHelper.GetCode(body));
    }

    [Fact]
    public async Task JwtClaimAlgorithmValidation_JwtWithArrayScope_IsRejected()
    {
        var key = $"test-{Guid.NewGuid():N}";

        using var request = ActionHttpHelper.CreatePaymentRequest(
            key,
            new
            {
                operationKind = "PAYMENT_EXECUTION",
                amount = "100.00",
                currency = "RUB",
            },
            TestJwt.CreateCandidateWithArrayScope()
        );

        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var document = await ActionHttpHelper.ReadJsonAsync(response);

        var body = document.RootElement;

        Assert.Equal("error", ActionHttpHelper.GetStatus(body));
        Assert.Equal("auth.invalid", ActionHttpHelper.GetCode(body));
    }

    [Fact]
    public async Task JwtClaimAlgorithmValidation_WithoutJwt_IsRejected()
    {
        var key = $"test-{Guid.NewGuid():N}";

        using var request = ActionHttpHelper.CreatePaymentRequest(
            key,
            new
            {
                operationKind = "PAYMENT_EXECUTION",
                amount = "100.00",
                currency = "RUB",
            },
            accessToken: null
        );

        using var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
