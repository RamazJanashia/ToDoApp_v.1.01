using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TodoApp.State;
using TodoCore;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace TodoApp;

/// <summary>
/// The attachment list of the selected task: Attach File… and Add Link, the link field, and a drop target for files
/// from File Explorer and links from a browser. Rows are kept per attachment. Switching task starts a fresh section.
/// </summary>
public sealed partial class AttachmentsSection : UserControl
{
    private readonly Dictionary<Guid, AttachmentRow> _rows = [];
    private AppState? _state;
    private Guid? _taskId;
    private int _count;
    private bool _addingLink;
    private bool _targeted;

    public AttachmentsSection()
    {
        InitializeComponent();
        AttachButton.Content = Glyphs.For("paperclip");
        LinkButton.Content = Glyphs.For("link");
        LinkFieldIcon.Glyph = Glyphs.For("link");

        AttachButton.Click += async (_, _) => await ChooseFilesAsync();
        LinkButton.Click += (_, _) =>
        {
            _addingLink = true;
            UpdateChrome();
            LinkBox.Focus(FocusState.Programmatic);
        };
        LinkBox.TextChanged += (_, _) => InvalidText.Visibility = Visibility.Collapsed;
        LinkBox.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                AddLink();
            }
            else if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                CloseLinkField();
            }
        };

        Root.DragEnter += OnDragOver;
        Root.DragOver += OnDragOver;
        Root.DragLeave += (_, _) => SetTargeted(false);
        Root.Drop += OnDrop;
    }

    public void Render(AppState state, Guid taskId, IReadOnlyList<Attachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(attachments);
        _state = state;
        if (_taskId != taskId)
        {
            _taskId = taskId;
            _rows.Clear();
            Rows.Children.Clear();
            _addingLink = false;
            _targeted = false;
            LinkBox.Text = "";
            InvalidText.Visibility = Visibility.Collapsed;
        }

        _count = attachments.Count;
        var ids = attachments.Select(a => a.Id).ToHashSet();
        foreach (var gone in _rows.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            Rows.Children.Remove(_rows[gone]);
            _rows.Remove(gone);
        }

        for (var i = 0; i < attachments.Count; i++)
        {
            var attachment = attachments[i];
            if (!_rows.TryGetValue(attachment.Id, out var row))
            {
                row = new AttachmentRow();
                row.Bind(state, taskId);
                _rows[attachment.Id] = row;
                Rows.Children.Insert(Math.Min(i, Rows.Children.Count), row);
            }

            row.Show(attachment);
        }

        UpdateChrome();
    }

    private void UpdateChrome()
    {
        LinkPanel.Visibility = _addingLink ? Visibility.Visible : Visibility.Collapsed;
        var showZone = _count == 0 && !_addingLink;
        Zone.Visibility = showZone ? Visibility.Visible : Visibility.Collapsed;
        ZoneTarget.Visibility = showZone && _targeted ? Visibility.Visible : Visibility.Collapsed;
        TargetFill.Visibility = _count > 0 && _targeted ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetTargeted(bool targeted)
    {
        if (_targeted == targeted) return;
        _targeted = targeted;
        UpdateChrome();
    }

    private async Task ChooseFilesAsync()
    {
        if (_state is not { } state || _taskId is not { } id) return;
        try
        {
            var picker = new FileOpenPicker { CommitButtonText = "Attach" };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowsShell.WindowHandle);
            var files = await picker.PickMultipleFilesAsync();
            if (files is null || files.Count == 0) return;
            state.Store.AttachFiles(files.Select(f => f.Path).ToList(), id);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            AppLog.Error("Couldn't show the file picker", ex);
        }
    }

    private void AddLink()
    {
        if (_state is not { } state || _taskId is not { } id) return;
        var text = LinkBox.Text;
        if (state.Store.AttachLink(text, id))
        {
            CloseLinkField();
        }
        else if (text.Trim().Length == 0)
        {
            CloseLinkField();
        }
        else
        {
            InvalidText.Visibility = Visibility.Visible;
        }
    }

    private void CloseLinkField()
    {
        LinkBox.Text = "";
        InvalidText.Visibility = Visibility.Collapsed;
        _addingLink = false;
        UpdateChrome();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var view = e.DataView;
        if (!view.Contains(StandardDataFormats.StorageItems) && !view.Contains(StandardDataFormats.WebLink)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Attach";
        e.Handled = true;
        SetTargeted(true);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        SetTargeted(false);
        if (_state is not { } state || _taskId is not { } id) return;
        var view = e.DataView;
        var deferral = e.GetDeferral();
        try
        {
            // A browser's link may come with a shortcut file too; the address is what the Mac attaches.
            if (view.Contains(StandardDataFormats.WebLink))
            {
                var uri = await view.GetWebLinkAsync();
                state.Store.AttachLink(uri.AbsoluteUri, id);
            }
            else if (view.Contains(StandardDataFormats.StorageItems))
            {
                var items = await view.GetStorageItemsAsync();
                var paths = items.OfType<StorageFile>().Select(f => f.Path).Where(p => p.Length > 0).ToList();
                if (paths.Count > 0) state.Store.AttachFiles(paths, id);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            AppLog.Error("Couldn't read what was dropped on the attachments", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
