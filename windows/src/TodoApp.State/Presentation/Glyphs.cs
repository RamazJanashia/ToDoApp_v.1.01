using System.Collections.Frozen;

namespace TodoApp.State;

/// <summary>
/// Maps the SF Symbol names the model and the Mac views use to Segoe Fluent Icons code points (Windows 11's
/// "Segoe Fluent Icons"; most are shared with "Segoe MDL2 Assets"). The closest metaphor was chosen for each.
/// </summary>
public static class Glyphs
{
    private const string Fallback = "folder";

    private static readonly FrozenDictionary<string, string> Table = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Project icon choices (Project.IconChoices)
        ["folder"] = "\uE8B7",
        ["briefcase"] = "\uE821",
        ["house"] = "\uE80F",
        ["person"] = "\uE77B",
        ["heart"] = "\uEB51",
        ["star"] = "\uE734",
        ["book"] = "\uE82D",
        ["cart"] = "\uE7BF",
        ["figure.run"] = "\uE805",
        ["lightbulb"] = "\uEA80",
        ["hammer"] = "\uE90F",
        ["airplane"] = "\uE709",
        ["graduationcap"] = "\uE7BE",
        ["leaf"] = "\uE8BE",
        ["music.note"] = "\uE8D6",
        ["gamecontroller"] = "\uE7FC",
        ["paintbrush"] = "\uE771",
        ["laptopcomputer"] = "\uE770",
        ["chart.bar"] = "\uE9D2",
        ["dollarsign.circle"] = "\uE8C7",
        ["camera"] = "\uE722",
        ["gift"] = "\uE719",
        ["tray.full"] = "\uE8C3",
        ["sparkles"] = "\uE794",

        // Sidebar and header
        ["tray"] = "\uE715",
        ["sun.max"] = "\uE706",
        ["calendar"] = "\uE787",
        ["checkmark.circle"] = "\uE930",
        ["square.grid.2x2"] = "\uE8A9",
        ["plus"] = "\uE710",
        ["list.bullet"] = "\uE8FD",
        ["rectangle.split.3x1"] = "\uE736",
        ["line.3.horizontal.decrease"] = "\uE71C",
        ["line.3.horizontal.decrease.circle.fill"] = "\uE71C",
        ["magnifyingglass"] = "\uE721",
        ["sidebar.right"] = "\uE90D",
        ["sidebar.left"] = "\uE90C",
        ["chevron.down"] = "\uE70D",
        ["xmark"] = "\uE711",
        ["ellipsis"] = "\uE712",
        ["xmark.circle.fill"] = "\uEB90",
        ["chevron.left"] = "\uE76B",
        ["chevron.right"] = "\uE76C",

        // Task details and rows
        ["info.circle"] = "\uE946",
        ["flag"] = "\uE7C1",
        ["repeat"] = "\uE8EE",
        ["bell"] = "\uEA8F",
        ["bell.fill"] = "\uE781",
        ["bell.slash"] = "\uE7ED",
        ["checklist"] = "\uE9D5",
        ["paperclip"] = "\uE723",
        ["link"] = "\uE71B",
        ["text.alignleft"] = "\uE8E4",
        ["trash"] = "\uE74D",
        ["plus.square.on.square"] = "\uE8C8",

        // Status, appearance and quick dates
        ["circle"] = "\uEA3A",
        ["circle.lefthalf.filled"] = "\uE7A1",
        ["moon"] = "\uE708",
        ["sunrise"] = "\uEC8A",
        ["arrow.right.to.line"] = "\uE8B5",
        ["nosign"] = "\uE733",
        ["slider.horizontal.3"] = "\uE9E9",
        ["square"] = "\uE739",
        ["checkmark.square.fill"] = "\uE73D",
        ["checkmark"] = "\uE73E",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Every SF Symbol name that has a glyph.</summary>
    public static IReadOnlyCollection<string> Known => Table.Keys;

    /// <summary>The glyph as a one-character string; an unknown name gives the folder.</summary>
    public static string For(string sfSymbol) =>
        sfSymbol is not null && Table.TryGetValue(sfSymbol, out var glyph) ? glyph : Table[Fallback];
}
