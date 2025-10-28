namespace MockBoard.Adapter;

public sealed class MockBoardOptions
{
    public string BoardId { get; set; } = "mockboard_eu";
    public string SigningSecret { get; set; } = "board-signing-secret";
}
