using FluentAssertions;
using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MockBoard.Adapter;
using MockBoard.Adapter.Services;

namespace Gateway.Test;

public class ReceiptSignatureTests
{
    private const string BoardSlug = "mockboard_eu";

    [Fact]
    public void ReceiptSignerProducesPayloadThatGatewayVerifierAccepts()
    {
        var payload = CreatePayload();
        var signer = CreateSigner();
        var signature = signer.Sign(payload, out var canonical);

        var verifier = CreateVerifier();
        verifier.VerifyDetached(canonical, signature, BoardSlug).Should().BeTrue();
    }

    [Fact]
    public void ReceiptSignatureFailsWhenPayloadIsTampered()
    {
        var payload = CreatePayload();
        var signer = CreateSigner();
        var signature = signer.Sign(payload, out var canonical);

        var tamperedPayload = payload with { Status = "pending-review" };
        var tamperedBytes = ReceiptJson.SerializePayload(tamperedPayload);

        var verifier = CreateVerifier();
        verifier.VerifyDetached(tamperedBytes, signature, BoardSlug).Should().BeFalse();
        verifier.VerifyDetached(canonical, signature, BoardSlug).Should().BeTrue();
    }

    private static ReceiptSigner CreateSigner()
    {
        var (mockboardRoot, _) = ResolvePaths();
        var options = new MockBoardOptions
        {
            BoardId = BoardSlug,
            PrivateJwkPath = Path.Combine(mockboardRoot, "certs", "mockboard_private.jwk.json")
        };
        var env = new TestHostEnvironment(mockboardRoot);
        return new ReceiptSigner(new StaticOptions(options), env);
    }

    private static IJwsVerifier CreateVerifier()
    {
        var (_, gatewayApiRoot) = ResolvePaths();
        var jwksPath = Path.Combine(gatewayApiRoot, "jwks", "mockboard.jwks.json");
        var jwksJson = File.ReadAllText(jwksPath);
        var jwks = new JsonWebKeySet(jwksJson);
        return new JwksJwsVerifier(new StaticKeyStore(BoardSlug, jwks));
    }

    private static (string MockBoardRoot, string GatewayApiRoot) ResolvePaths()
    {
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var mockBoardRoot = Path.Combine(solutionRoot, "src", "MockBoard.Adapter");
        var gatewayApiRoot = Path.Combine(solutionRoot, "src", "Gateway.Api");
        return (mockBoardRoot, gatewayApiRoot);
    }

    private static BoardReceiptPayload CreatePayload() =>
        new(
            Spec: "consent-apply/v0.1",
            ApplicationId: Guid.NewGuid().ToString(),
            BoardId: BoardSlug,
            JobExternalId: "job-123",
            CandidateId: Guid.NewGuid().ToString(),
            Status: "accepted",
            ReceivedAt: new DateTime(2025, 10, 28, 17, 30, 0, DateTimeKind.Utc),
            BoardRef: "MB-123456");

    private sealed class StaticOptions : IOptions<MockBoardOptions>
    {
        public StaticOptions(MockBoardOptions value) => Value = value;
        public MockBoardOptions Value { get; }
    }

    private sealed class StaticKeyStore : ITenantKeyStore
    {
        private readonly Dictionary<string, JsonWebKeySet> _map;

        public StaticKeyStore(string slug, JsonWebKeySet jwks)
        {
            _map = new Dictionary<string, JsonWebKeySet>(StringComparer.Ordinal)
            {
                [slug] = jwks
            };
        }

        public bool TryGetKeys(string tenantSlug, out JsonWebKeySet? jwks) =>
            _map.TryGetValue(tenantSlug, out jwks);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRoot)
        {
            ContentRootPath = contentRoot;
            ContentRootFileProvider = new PhysicalFileProvider(contentRoot);
        }

        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "MockBoard.Adapter";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
