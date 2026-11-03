namespace TodoCore;

public sealed record Project
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    /// <summary>An SF Symbol name.</summary>
    public string Icon { get; init; } = "folder";

    public ProjectColor Color { get; init; } = ProjectColor.Blue;

    /// <summary>The project this one sits under; null for a top-level project.</summary>
    public Guid? ParentId { get; init; }

    /// <summary>Symbols offered in the project editor.</summary>
    public static IReadOnlyList<string> IconChoices { get; } = new[]
    {
        "folder", "briefcase", "house", "person", "heart", "star",
        "book", "cart", "figure.run", "lightbulb", "hammer", "airplane",
        "graduationcap", "leaf", "music.note", "gamecontroller", "paintbrush", "laptopcomputer",
        "chart.bar", "dollarsign.circle", "camera", "gift", "tray.full", "sparkles",
    };
}

/// <summary>A restrained palette; values tuned to sit quietly on a near-black background.</summary>
public enum ProjectColor
{
    Blue, Green, Red, Orange, Yellow, Purple, Pink, Teal, Graphite, White,
}

public static class ProjectColors
{
    /// <summary>The colour as 0xRRGGBB (the Mac's <c>hex</c>).</summary>
    public static uint DarkHex(this ProjectColor color) => color switch
    {
        ProjectColor.Blue => 0x4C8DF6,
        ProjectColor.Green => 0x4CC38A,
        ProjectColor.Red => 0xEF5B53,
        ProjectColor.Orange => 0xF2994A,
        ProjectColor.Yellow => 0xF2C94C,
        ProjectColor.Purple => 0xA78BFA,
        ProjectColor.Pink => 0xF472B6,
        ProjectColor.Teal => 0x4FD1C5,
        ProjectColor.Graphite => 0x8E8E93,
        ProjectColor.White => 0xE6E6EA,
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
    };
}
