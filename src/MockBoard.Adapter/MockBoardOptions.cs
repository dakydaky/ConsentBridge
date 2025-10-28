namespace MockBoard.Adapter;

public sealed class MockBoardOptions
{
    public string BoardId { get; set; } = "mockboard_eu";
    public string PrivateJwkPath { get; set; } = "certs/mockboard_private.jwk.json";
}
