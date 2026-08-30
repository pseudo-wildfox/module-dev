using System.Net;
using Api.Tests.Helpers;

namespace Api.Tests.Contract;

public sealed class ActionPublicationContractTests
    : ApiTestBase
{
    public ActionPublicationContractTests(
        ApiTestFixture fixture)
        : base(fixture)
    {
    }


    [Fact]
    public async Task Publish_SameManifestTwice_IsIdempotent()
    {
        var module =
            TestActionName.CreateModule();

        var manifestPath =
            Cli.CreateManifestFile(
                module,
                "echo",
                version: 1);

        try
        {
            var first =
                await Cli.PublishAsync(
                    manifestPath);

            Assert.Equal(
                0,
                first.ExitCode);

            Assert.Equal(
                "ok",
                first.Status);

            Assert.Equal(
                "action",
                first.Document!
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("resource")
                    .GetString());

            var second =
                await Cli.PublishAsync(
                    manifestPath);

            Assert.Equal(
                0,
                second.ExitCode);

            Assert.Equal(
                "ok",
                second.Status);

            Assert.Equal(
                "published",
                second.Document!
                    .RootElement
                    .GetProperty("result")
                    .GetProperty("operation")
                    .GetString());
        }
        finally
        {
            Cli.DeleteManifestFile(
                manifestPath);
        }
    }


    [Fact]
    public async Task Publish_ChangedManifestSameVersion_ReturnsConflict()
    {
        var module =
            TestActionName.CreateModule();

        var originalManifest =
            Cli.CreateManifestFile(
                module,
                "echo",
                version: 1,
                timeoutMs: 5000);

        var changedManifest =
            Cli.CreateManifestFile(
                module,
                "echo",
                version: 1,
                timeoutMs: 10000);

        try
        {
            var first =
                await Cli.PublishAsync(
                    originalManifest);

            Assert.Equal(
                0,
                first.ExitCode);

            var conflict =
                await Cli.PublishAsync(
                    changedManifest);

            Assert.NotEqual(
                0,
                conflict.ExitCode);

            Assert.Equal(
                "error",
                conflict.Status);

            Assert.Equal(
                "manifest.conflict",
                conflict.Code);
        }
        finally
        {
            Cli.DeleteManifestFile(
                originalManifest);

            Cli.DeleteManifestFile(
                changedManifest);
        }
    }


    [Fact]
    public async Task ExplicitVersion_ExecutesSelectedPublishedVersion()
    {
        var module =
            TestActionName.CreateModule();

        var v1 =
            Cli.CreateManifestFile(
                module,
                "echo",
                version: 1);

        var v2 =
            Cli.CreateManifestFile(
                module,
                "echo",
                version: 2);

        var key1 =
            $"version-1-{Guid.NewGuid():N}";

        var key2 =
            $"version-2-{Guid.NewGuid():N}";

        try
        {
            Assert.Equal(
                0,
                (await Cli.PublishAsync(v1)).ExitCode);

            Assert.Equal(
                0,
                (await Cli.PublishAsync(v2)).ExitCode);

            Assert.Equal(
                0,
                (await Cli.ActivateAsync(
                    $"{module}.echo",
                    1)).ExitCode);

            Assert.Equal(
                0,
                (await Cli.ActivateAsync(
                    $"{module}.echo",
                    2)).ExitCode);

            using var requestV1 =
                ActionHttpHelper.CreateGenericRequest(
                    $"/api/{module}/echo",
                    new
                    {
                        value = "version-1"
                    },
                    TestJwt.CreateCandidate(),
                    key1);

            requestV1.Headers.Add(
                "X-Action-Version",
                "1");

            using var responseV1 =
                await Client.SendAsync(requestV1);

            Assert.Equal(
                HttpStatusCode.OK,
                responseV1.StatusCode);

            using var documentV1 =
                await ActionHttpHelper.ReadJsonAsync(
                    responseV1);

            Assert.Equal(
                1,
                documentV1.RootElement
                    .GetProperty("meta")
                    .GetProperty("actionVersion")
                    .GetInt32());

            Assert.Equal(
                "version-1",
                documentV1.RootElement
                    .GetProperty("result")
                    .GetProperty("echo")
                    .GetProperty("value")
                    .GetString());


            using var requestV2 =
                ActionHttpHelper.CreateGenericRequest(
                    $"/api/{module}/echo",
                    new
                    {
                        value = "version-2"
                    },
                    TestJwt.CreateCandidate(),
                    key2);

            requestV2.Headers.Add(
                "X-Action-Version",
                "2");

            using var responseV2 =
                await Client.SendAsync(requestV2);

            Assert.Equal(
                HttpStatusCode.OK,
                responseV2.StatusCode);

            using var documentV2 =
                await ActionHttpHelper.ReadJsonAsync(
                    responseV2);

            Assert.Equal(
                2,
                documentV2.RootElement
                    .GetProperty("meta")
                    .GetProperty("actionVersion")
                    .GetInt32());

            Assert.Equal(
                "version-2",
                documentV2.RootElement
                    .GetProperty("result")
                    .GetProperty("echo")
                    .GetProperty("value")
                    .GetString());
        }
        finally
        {
            Cli.DeleteManifestFile(v1);
            Cli.DeleteManifestFile(v2);
        }
    }


    [Fact]
    public async Task Activate_ChangesDefaultVersion()
    {
        var module =
            TestActionName.CreateModule();

        var v1 =
            Cli.CreateManifestFile(
                module,
                "echo",
                version: 1);

        var v2 =
            Cli.CreateManifestFile(
                module,
                "echo",
                version: 2);

        try
        {
            Assert.Equal(
                0,
                (await Cli.PublishAsync(v1)).ExitCode);

            Assert.Equal(
                0,
                (await Cli.PublishAsync(v2)).ExitCode);

            Assert.Equal(
                0,
                (await Cli.ActivateAsync(
                    $"{module}.echo",
                    1)).ExitCode);

            var firstKey =
                $"default-v1-{Guid.NewGuid():N}";

            using var firstRequest =
                ActionHttpHelper.CreateGenericRequest(
                    $"/api/{module}/echo",
                    new
                    {
                        value = "default-v1"
                    },
                    TestJwt.CreateCandidate(),
                    firstKey);

            using var firstResponse =
                await Client.SendAsync(firstRequest);

            Assert.Equal(
                HttpStatusCode.OK,
                firstResponse.StatusCode);

            using var firstDocument =
                await ActionHttpHelper.ReadJsonAsync(
                    firstResponse);

            Assert.Equal(
                1,
                firstDocument.RootElement
                    .GetProperty("meta")
                    .GetProperty("actionVersion")
                    .GetInt32());


            Assert.Equal(
                0,
                (await Cli.ActivateAsync(
                    $"{module}.echo",
                    2)).ExitCode);

            var secondKey =
                $"default-v2-{Guid.NewGuid():N}";

            using var secondRequest =
                ActionHttpHelper.CreateGenericRequest(
                    $"/api/{module}/echo",
                    new
                    {
                        value = "default-v2"
                    },
                    TestJwt.CreateCandidate(),
                    secondKey);

            using var secondResponse =
                await Client.SendAsync(secondRequest);

            Assert.Equal(
                HttpStatusCode.OK,
                secondResponse.StatusCode);

            using var secondDocument =
                await ActionHttpHelper.ReadJsonAsync(
                    secondResponse);

            Assert.Equal(
                2,
                secondDocument.RootElement
                    .GetProperty("meta")
                    .GetProperty("actionVersion")
                    .GetInt32());
        }
        finally
        {
            Cli.DeleteManifestFile(v1);
            Cli.DeleteManifestFile(v2);
        }
    }


    [Fact]
    public async Task DisableDefaultVersion_WithReplacement_MakesOldVersionUnavailable()
    {
        var module =
            TestActionName.CreateModule();

        var v1 =
            Cli.CreateManifestFile(
                module,
                "echo",
                version: 1);

        var v2 =
            Cli.CreateManifestFile(
                module,
                "echo",
                version: 2);

        try
        {
            Assert.Equal(
                0,
                (await Cli.PublishAsync(v1)).ExitCode);

            Assert.Equal(
                0,
                (await Cli.PublishAsync(v2)).ExitCode);

            Assert.Equal(
                0,
                (await Cli.ActivateAsync(
                    $"{module}.echo",
                    1)).ExitCode);

            Assert.Equal(
                0,
                (await Cli.ActivateAsync(
                    $"{module}.echo",
                    2)).ExitCode);

            var disabled =
                await Cli.DisableAsync(
                    $"{module}.echo",
                    1,
                    replacementVersion: 2);

            Assert.Equal(
                0,
                disabled.ExitCode);

            Assert.Equal(
                "ok",
                disabled.Status);


            using var oldRequest =
                ActionHttpHelper.CreateGenericRequest(
                    $"/api/{module}/echo",
                    new
                    {
                        value = "disabled"
                    },
                    TestJwt.CreateCandidate(),
                    $"disabled-{Guid.NewGuid():N}");

            oldRequest.Headers.Add(
                "X-Action-Version",
                "1");

            using var oldResponse =
                await Client.SendAsync(
                    oldRequest);

            Assert.Equal(
                HttpStatusCode.NotFound,
                oldResponse.StatusCode);

            using var oldDocument =
                await ActionHttpHelper.ReadJsonAsync(
                    oldResponse);

            Assert.Equal(
                "action.not_found",
                ActionHttpHelper.GetCode(
                    oldDocument.RootElement));


            using var replacementRequest =
                ActionHttpHelper.CreateGenericRequest(
                    $"/api/{module}/echo",
                    new
                    {
                        value = "replacement"
                    },
                    TestJwt.CreateCandidate(),
                    $"replacement-{Guid.NewGuid():N}");

            replacementRequest.Headers.Add(
                "X-Action-Version",
                "2");

            using var replacementResponse =
                await Client.SendAsync(
                    replacementRequest);

            Assert.Equal(
                HttpStatusCode.OK,
                replacementResponse.StatusCode);
        }
        finally
        {
            Cli.DeleteManifestFile(v1);
            Cli.DeleteManifestFile(v2);
        }
    }


    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "ModuleDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }
}