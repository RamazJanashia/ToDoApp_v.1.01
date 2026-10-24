using TodoCore;

namespace TodoApp.State.Tests;

public sealed class GlyphsTests
{
    private static readonly string[] UiIcons =
    {
        "tray", "sun.max", "calendar", "checkmark.circle", "square.grid.2x2", "plus", "info.circle", "flag", "repeat",
        "bell", "bell.fill", "bell.slash", "checklist", "paperclip", "link", "text.alignleft", "list.bullet",
        "rectangle.split.3x1", "line.3.horizontal.decrease", "magnifyingglass", "sidebar.right", "xmark", "trash",
        "plus.square.on.square", "chevron.left", "chevron.right", "circle", "circle.lefthalf.filled", "moon", "sunrise",
        "arrow.right.to.line", "nosign", "slider.horizontal.3", "square", "checkmark.square.fill", "checkmark",
    };

    [Fact]
    public void Glyphs_CoverEveryProjectIconAndUiIcon()
    {
        Assert.Equal(24, Project.IconChoices.Count);
        foreach (var name in Project.IconChoices.Concat(UiIcons))
        {
            Assert.Contains(name, Glyphs.Known);
        }
    }

    [Fact]
    public void EveryGlyphIsOnePrivateUseCharacter()
    {
        foreach (var name in Glyphs.Known)
        {
            var glyph = Glyphs.For(name);
            Assert.Equal(1, glyph.Length);
            Assert.InRange(glyph[0], '', '');
        }
    }

    [Fact]
    public void UnknownNamesFallBackToTheFolder()
    {
        Assert.Equal(Glyphs.For("folder"), Glyphs.For("no.such.symbol"));
        Assert.Equal(Glyphs.For("folder"), Glyphs.For(""));
        Assert.NotEqual(Glyphs.For("folder"), Glyphs.For("tray"));
    }

    [Fact]
    public void ProjectIconsCanBeToldApart()
    {
        // Two project icons sharing a glyph would make the picker look broken.
        var glyphs = Project.IconChoices.Select(Glyphs.For).ToList();

        Assert.Equal(glyphs.Count, glyphs.Distinct().Count());
    }
}
