namespace Gateway.Domain;

public interface IJwsVerifier
{
    bool VerifyDetached(ReadOnlySpan<byte> canonicalJson, string jws, string expectedKid);
}

public sealed class AcceptAllVerifier : IJwsVerifier
{
    public bool VerifyDetached(ReadOnlySpan<byte> canonicalJson, string jws, string expectedKid) => true; // demo only
}
