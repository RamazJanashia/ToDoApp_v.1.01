using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;
using Windows.ApplicationModel.DataTransfer;

namespace TodoApp;

/// <summary>
/// One attachment: a click opens it, the context menu offers Open, Show in File Explorer (files) or Copy Link (links)
/// and Remove. The kind and size of a file and its thumbnail arrive a moment after the row does.
/// </summary>
public sealed partial class AttachmentRow : UserControl
{
    private AppState? _state;
    private Guid _taskId;
    private Attachment? _attachment;
    private string? _path;
    private bool _hovering;

    public AttachmentRow()
    {
        InitializeComponent();
        RemoveButton.Content = Glyphs.For("xmark");
        LinkIcon.Glyph = Glyphs.For("link");

        Root.Tapped += (_, _) => Open();
        Root.PointerEntered += (_, _) => SetHover(true);
        Root.PointerExited += (_, _) => SetHover(false);
        RemoveButton.Tapped += (_, e) => e.Handled = true;
        RemoveButton.Click += (_, _) => Remove();
        RemoveButton.GotFocus += (_, _) => RemoveButton.Opacity = 1;
        RemoveButton.LostFocus += (_, _) => RemoveButton.Opacity = _hovering ? 1 : 0;
        NameHost.SizeChanged += (_, _) => FitName();

        OpenItem.Click += (_, _) => Open();
        RevealItem.Click += (_, _) =>
        {
            if (_path is not null) WindowsShell.RevealInExplorer(_path);
        };
        CopyItem.Click += (_, _) =>
        {
            if (_attachment is not { } attachment) return;
            var data = new DataPackage();
            data.SetText(attachment.Location);
            Clipboard.SetContent(data);
        };
        RemoveItem.Click += (_, _) => Remove();
        Menu.Opening += (_, _) =>
        {
            var isFile = _attachment?.Kind == AttachmentKind.File;
            RevealItem.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
            CopyItem.Visibility = isFile ? Visibility.Collapsed : Visibility.Visible;
        };
    }

    public void Bind(AppState state, Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _taskId = taskId;
    }

    public void Show(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (_attachment?.Id == attachment.Id) return;
        _attachment = attachment;
        _path = _state?.Store.Attachments.PathFor(attachment);
        FitName();

        var tip = attachment.Kind == AttachmentKind.Link ? attachment.Location : "Open " + attachment.Name;
        ToolTipService.SetToolTip(Root, tip);

        if (attachment.Kind == AttachmentKind.Link)
        {
            LinkIcon.Visibility = Visibility.Visible;
            ThumbImage.Source = null;
            DetailText.Text = Uri.TryCreate(attachment.Location, UriKind.Absolute, out var uri) && uri.Host.Length > 0
                ? uri.Host
                : attachment.Location;
            return;
        }

        LinkIcon.Visibility = Visibility.Collapsed;
        ThumbImage.Source = null;
        DetailText.Text = "";
        _ = LoadFileDetailsAsync(attachment, _path);
    }

    private async Task LoadFileDetailsAsync(Attachment attachment, string? path)
    {
        if (path is null) return;
        var kind = await WindowsShell.FileTypeDescriptionAsync(path);
        var info = new FileInfo(path);
        var detail = info.Exists ? $"{kind} · {AttachmentStorage.FileSize(info.Length)}" : $"{kind} · missing";
        var thumbnail = info.Exists ? await WindowsShell.ThumbnailAsync(attachment, path) : null;
        // The row may have been handed another attachment while the shell was answering.
        if (_attachment?.Id != attachment.Id) return;
        DetailText.Text = detail;
        ThumbImage.Source = thumbnail;
    }

    private void Open()
    {
        if (_attachment is not { } attachment) return;
        var target = attachment.Kind == AttachmentKind.Link ? attachment.Location : InsideStorage(_path);
        if (target is not null) _ = WindowsShell.OpenAsync(target);
    }

    /// <summary>
    /// The path when it really is inside the attachments folder, otherwise null: a hand-edited tasks.json can hold a
    /// location like "..\..\tool.exe", and an attachment must never open anything outside its own folder.
    /// </summary>
    private string? InsideStorage(string? path)
    {
        if (path is null || _state is not { } state) return null;
        var root = Path.GetFullPath(state.Store.Attachments.Directory).TrimEnd('\\') + '\\';
        var full = WindowsShell.LocalFile(path);
        return full is not null && full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private void Remove()
    {
        if (_state is { } state && _attachment is { } attachment) state.Store.RemoveAttachment(attachment.Id, _taskId);
    }

    private void SetHover(bool hovering)
    {
        _hovering = hovering;
        HoverFill.Opacity = hovering ? 1 : 0;
        RemoveButton.Opacity = hovering ? 1 : 0;
    }

    /// <summary>Shortens a long name in the middle, so the start and the extension both stay readable.</summary>
    private void FitName()
    {
        if (_attachment is not { } attachment) return;
        var width = NameHost.ActualWidth;
        var name = attachment.Name;
        NameText.Text = name;
        if (width <= 0 || Measure(name) <= width) return;

        var low = 0;
        var high = name.Length - 1;
        while (low < high)
        {
            var kept = (low + high + 1) / 2;
            if (Measure(Shortened(name, kept)) <= width) low = kept;
            else high = kept - 1;
        }

        NameText.Text = Shortened(name, low);
    }

    private static string Shortened(string name, int kept)
    {
        var head = (kept + 1) / 2;
        var tail = kept / 2;
        return name[..head] + "…" + name[^tail..];
    }

    private double Measure(string text)
    {
        NameText.Text = text;
        NameText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return NameText.DesiredSize.Width;
    }
}
