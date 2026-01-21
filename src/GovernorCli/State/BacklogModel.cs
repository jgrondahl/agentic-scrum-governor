namespace GovernorCli.State;

public sealed class BacklogFile
{
    public List<BacklogItem> Backlog { get; set; } = new();
}

public sealed class BacklogItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "candidate"; // candidate | ready | in_sprint | done
    public int Priority { get; set; } = 0;
    public string Size { get; set; } = "S";          // S | M | L
    public string Owner { get; set; } = "PO";        // PO | SAD | SASD | QA | MIBS

    public string Story { get; set; } = "";
    public List<string> Acceptance_Criteria { get; set; } = new();
    public List<string> Non_Goals { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public List<string> Risks { get; set; } = new();
}
