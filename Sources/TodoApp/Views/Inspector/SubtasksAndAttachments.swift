import AppKit
import SwiftUI
import TodoCore

struct SubtasksSection: View {
    @EnvironmentObject private var store: TaskStore

    let taskID: UUID
    let subtasks: [Subtask]
    let progress: Double

    @State private var newTitle = ""
    @FocusState private var addFieldFocused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                SectionHeader("Subtasks")
                Spacer()
                if !subtasks.isEmpty {
                    Text("\(subtasks.filter(\.isDone).count)/\(subtasks.count)")
                        .font(Theme.Font.small)
                        .monospacedDigit()
                        .foregroundColor(Theme.tertiaryText)
                }
            }
            .padding(.top, 30)
            .padding(.bottom, 8)

            if !subtasks.isEmpty {
                ProgressLine(fraction: progress)
                    .padding(.bottom, 8)
            }

            // Lazy, so a long checklist only builds the rows on screen.
            LazyVStack(alignment: .leading, spacing: 0) {
                ForEach(subtasks) { subtask in
                    SubtaskRow(store: store, taskID: taskID, subtask: subtask)
                        .equatable()
                }
            }

            HStack(alignment: .top, spacing: 12) {
                Image(systemName: "plus")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(Theme.tertiaryText)
                    .frame(width: 20, height: 17)
                TextField("Add subtask", text: $newTitle, axis: .vertical)
                    .textFieldStyle(.plain)
                    .font(Theme.Font.body)
                    .lineLimit(1...)
                    .focused($addFieldFocused)
                    .onSubmit(add)
            }
            .padding(.vertical, 8)
        }
    }

    private func add() {
        let title = newTitle.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !title.isEmpty else { return }
        store.updateTask(taskID, undoName: "Add Subtask") { $0.subtasks.append(Subtask(title: title)) }
        newTitle = ""
        addFieldFocused = true
    }
}

/// Holds the store without observing it, and compares by its subtask, so an edit elsewhere
/// doesn't redraw every row of a long checklist.
struct SubtaskRow: View, Equatable {
    let store: TaskStore
    let taskID: UUID
    let subtask: Subtask

    @State private var hovering = false

    static func == (lhs: Self, rhs: Self) -> Bool {
        lhs.store === rhs.store && lhs.taskID == rhs.taskID && lhs.subtask == rhs.subtask
    }

    private var titleBinding: Binding<String> {
        Binding(
            get: { subtask.title },
            set: { newValue in
                store.updateTask(taskID) { task in
                    if let index = task.subtasks.firstIndex(where: { $0.id == subtask.id }) {
                        task.subtasks[index].title = newValue
                    }
                }
            }
        )
    }

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            CheckSquare(isDone: subtask.isDone) {
                store.updateTask(taskID, undoName: "Toggle Subtask") { task in
                    if let index = task.subtasks.firstIndex(where: { $0.id == subtask.id }) {
                        task.subtasks[index].isDone.toggle()
                    }
                }
            }
            TextField("Subtask", text: titleBinding, axis: .vertical)
                .textFieldStyle(.plain)
                .font(Theme.Font.body)
                .foregroundColor(subtask.isDone ? Theme.tertiaryText : Theme.primaryText)
                .strikethrough(subtask.isDone, color: Theme.tertiaryText)
                .lineLimit(1...)
                .frame(maxWidth: .infinity, alignment: .leading)
            Button {
                store.updateTask(taskID, undoName: "Delete Subtask") { task in
                    task.subtasks.removeAll { $0.id == subtask.id }
                }
            } label: {
                Image(systemName: "xmark")
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundColor(Theme.tertiaryText)
                    .frame(width: 20, height: 20)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .opacity(hovering ? 1 : 0)
            .help("Delete Subtask")
            .accessibilityLabel("Delete Subtask")
        }
        .padding(.vertical, 8)
        .onHover { hovering = $0 }
    }
}

struct AttachmentsSection: View {
    @EnvironmentObject private var store: TaskStore

    let taskID: UUID
    let attachments: [Attachment]

    @State private var isAddingLink = false
    @State private var linkText = ""
    @State private var linkIsInvalid = false
    @State private var isDropTarget = false
    @FocusState private var linkFocused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 2) {
                SectionHeader("Attachments")
                Spacer()
                IconButton(systemName: "paperclip", size: 13, help: "Attach File…", action: chooseFiles)
                IconButton(systemName: "link", size: 13, help: "Add Link") {
                    isAddingLink = true
                    DispatchQueue.main.async { linkFocused = true }
                }
            }
            .padding(.top, 26)
            .padding(.bottom, 6)

            ForEach(attachments) { attachment in
                AttachmentRow(store: store, taskID: taskID, attachment: attachment)
                    .equatable()
            }

            if isAddingLink {
                HStack(spacing: 10) {
                    Image(systemName: "link")
                        .font(.system(size: 12))
                        .foregroundColor(Theme.tertiaryText)
                        .frame(width: 30)
                    TextField("Paste a link and press Return", text: $linkText)
                        .textFieldStyle(.plain)
                        .font(.system(size: 13))
                        .focused($linkFocused)
                        .onSubmit(addLink)
                        .onExitCommand(perform: closeLinkField)
                        .onChange(of: linkText) { _ in linkIsInvalid = false }
                }
                .frame(height: 36)
                if linkIsInvalid {
                    Text("That doesn’t look like a web or mail address.")
                        .font(Theme.Font.small)
                        .foregroundColor(Theme.red)
                        .padding(.leading, 40)
                }
            }

            if attachments.isEmpty && !isAddingLink {
                Text("Drop files here, or use the buttons above.")
                    .font(Theme.Font.caption)
                    .foregroundColor(Theme.tertiaryText)
                    .frame(maxWidth: .infinity, minHeight: 54)
                    .background(
                        RoundedRectangle(cornerRadius: 8, style: .continuous)
                            .strokeBorder(style: StrokeStyle(lineWidth: 1, dash: [4, 4]))
                            .foregroundColor(isDropTarget ? Theme.blue.opacity(0.7) : Theme.strongSeparator)
                    )
                    .padding(.top, 4)
            }
        }
        .background(
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(isDropTarget && !attachments.isEmpty ? Theme.blue.opacity(0.08) : .clear)
                .padding(.horizontal, -8)
        )
        .dropDestination(for: URL.self) { urls, _ in
            let files = urls.filter(\.isFileURL)
            let links = urls.filter { !$0.isFileURL }
            store.attachFiles(files, to: taskID)
            let accepted = links.filter { store.attachLink($0.absoluteString, to: taskID) }
            return !files.isEmpty || !accepted.isEmpty
        } isTargeted: { isDropTarget = $0 }
    }

    private func chooseFiles() {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        panel.prompt = "Attach"
        panel.message = "Choose documents or images to attach. Copies are kept with your tasks."
        if panel.runModal() == .OK {
            store.attachFiles(panel.urls, to: taskID)
        }
    }

    private func addLink() {
        if store.attachLink(linkText, to: taskID) {
            closeLinkField()
        } else {
            linkIsInvalid = !linkText.trimmingCharacters(in: .whitespaces).isEmpty
            if linkText.trimmingCharacters(in: .whitespaces).isEmpty { closeLinkField() }
        }
    }

    private func closeLinkField() {
        linkText = ""
        linkIsInvalid = false
        isAddingLink = false
    }
}

/// Like `SubtaskRow`, redraws only when its attachment changes: drawing it reads the file's
/// size and kind from disk, which shouldn't happen on every keystroke elsewhere.
struct AttachmentRow: View, Equatable {
    let store: TaskStore
    let taskID: UUID
    let attachment: Attachment

    @State private var hovering = false

    static func == (lhs: Self, rhs: Self) -> Bool {
        lhs.store === rhs.store && lhs.taskID == rhs.taskID && lhs.attachment == rhs.attachment
    }

    private var url: URL? { store.attachments.url(for: attachment) }

    var body: some View {
        HStack(spacing: 10) {
            thumbnail
                .frame(width: 30, height: 30)
                .clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))
            VStack(alignment: .leading, spacing: 2) {
                Text(attachment.name)
                    .font(.system(size: 13))
                    .foregroundColor(Theme.primaryText)
                    .lineLimit(1)
                    .truncationMode(.middle)
                Text(store.attachments.detail(for: attachment))
                    .font(Theme.Font.small)
                    .foregroundColor(Theme.tertiaryText)
                    .lineLimit(1)
            }
            Spacer(minLength: 4)
            Button {
                store.removeAttachment(attachment.id, from: taskID)
            } label: {
                Image(systemName: "xmark")
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundColor(Theme.tertiaryText)
                    .frame(width: 20, height: 20)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .opacity(hovering ? 1 : 0)
            .help("Remove Attachment")
            .accessibilityLabel("Remove Attachment")
        }
        .padding(.vertical, 6)
        .padding(.horizontal, 6)
        .rowHighlight(selected: false, hovering: hovering, cornerRadius: 7)
        .padding(.horizontal, -6)
        .contentShape(Rectangle())
        .onTapGesture(perform: open)
        .onHover { hovering = $0 }
        .help(attachment.kind == .link ? attachment.location : "Open \(attachment.name)")
        .accessibleRow("Open \(attachment.name)", action: open)
        .contextMenu {
            Button("Open", action: open)
            if attachment.kind == .file {
                if let url {
                    Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([url]) }
                }
            } else {
                Button("Copy Link") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(attachment.location, forType: .string)
                }
            }
            Divider()
            Button("Remove", role: .destructive) { store.removeAttachment(attachment.id, from: taskID) }
        }
    }

    @ViewBuilder
    private var thumbnail: some View {
        if attachment.kind == .link {
            Image(systemName: "link")
                .font(.system(size: 12))
                .foregroundColor(Theme.secondaryText)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(Theme.raised)
        } else if let image = store.attachments.thumbnail(for: attachment) {
            Image(nsImage: image)
                .resizable()
                .aspectRatio(contentMode: .fill)
        } else if let url {
            Image(nsImage: NSWorkspace.shared.icon(forFile: url.path))
                .resizable()
                .aspectRatio(contentMode: .fit)
                .padding(2)
        }
    }

    private func open() {
        guard let url else { return }
        NSWorkspace.shared.open(url)
    }
}
