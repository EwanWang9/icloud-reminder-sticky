namespace ICloudReminderSticky;

public sealed class ReminderSnapshot
{
    public int Index { get; set; }
    public int Occurrence { get; set; }
    public string Title { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class ReminderPayload
{
    public bool Ready { get; set; }
    public List<ReminderSnapshot> Items { get; set; } = [];
}

public sealed class CompletionTarget
{
    public bool Found { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}
