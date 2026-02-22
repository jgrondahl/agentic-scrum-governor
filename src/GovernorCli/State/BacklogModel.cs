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

    public BacklogEstimate? Estimate { get; set; }
    public string? Technical_Notes_Ref { get; set; }

    public string Story { get; set; } = "";
    public List<string> Acceptance_Criteria { get; set; } = new();
    public List<string> Non_Goals { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public List<string> Risks { get; set; } = new();
}

public sealed class BacklogEstimate
{
    public string Id { get; set; } = "";
    public int Story_Points { get; set; }
    public string Scale { get; set; } = "fibonacci";
    public string Confidence { get; set; } = "medium";
    public string Risk_Level { get; set; } = "medium";
    public List<string> Complexity_Drivers { get; set; } = new();
    public List<string> Assumptions { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public List<string> Non_Goals { get; set; } = new();
    public string Notes { get; set; } = "";
    public string Created_At_Utc { get; set; } = "";
    public string Created_From_Run_Id { get; set; } = "";
}