using FluentAssertions;
using Gateway.Domain;
using Gateway.Infrastructure;
using Microsoft.Extensions.Options;
using MockBoard.Adapter;
using MockBoard.Adapter.Services;

namespace Gateway.Test;

public class ReceiptSignatureTests
{
    private const string BoardSlug = "mockboard_eu";
    private const string SigningSecret = "board-signing-secret";

    [Fact]
    public void ReceiptSignerProducesPayloadThatGatewayVerifierAccepts()
    {
        var payload = CreatePayload();
        var signer = new ReceiptSigner(new StaticOptions(new MockBoardOptions
        {
            BoardId = BoardSlug,
            SigningSecret = SigningSecret
        }));

        var signature = signer.Sign(payload, out var canonical);

        var verifier = new Hs256JwsVerifier(new InMemorySecretProvider(BoardSlug, SigningSecret));
        verifier.VerifyDetached(canonical, signature, BoardSlug).Should().BeTrue();
    }

    [Fact]
    public void ReceiptSignatureFailsWhenPayloadIsTampered()
    {
        var payload = CreatePayload();
        var signer = new ReceiptSigner(new StaticOptions(new MockBoardOptions
        {
            BoardId = BoardSlug,
            SigningSecret = SigningSecret
        }));

        var signature = signer.Sign(payload, out var canonical);

        var tamperedPayload = payload with { Status = "pending-review" };
        var tamperedBytes = ReceiptJson.SerializePayload(tamperedPayload);

        var verifier = new Hs256JwsVerifier(new InMemorySecretProvider(BoardSlug, SigningSecret));
        verifier.VerifyDetached(tamperedBytes, signature, BoardSlug).Should().BeFalse();

        // Original payload still verifies
        verifier.VerifyDetached(canonical, signature, BoardSlug).Should().BeTrue();
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

    private sealed class InMemorySecretProvider : ITenantSigningSecretProvider
    {
        private readonly string _tenantSlug;
        private readonly string _secret;

        public InMemorySecretProvider(string tenantSlug, string secret)
        {
            _tenantSlug = tenantSlug;
            _secret = secret;
        }

        public bool TryGetSigningSecret(string tenantSlug, out string? secret)
        {
            if (string.Equals(tenantSlug, _tenantSlug, StringComparison.Ordinal))
            {
                secret = _secret;
                return true;
            }

            secret = null;
            return false;
        }
    }
}
