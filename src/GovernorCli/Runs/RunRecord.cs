namespace GovernorCli.Runs;

public sealed class RunRecord
{
    public string RunId { get; set; } = "";
    public string Flow { get; set; } = "";
    public string CreatedAtUtc { get; set; } = "";
    public string Workdir { get; set; } = "";
    public int ItemId { get; set; }
    public string ItemTitle { get; set; } = "";
    public string Status { get; set; } = "";
}
