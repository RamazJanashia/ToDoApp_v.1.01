import SwiftUI
import TodoCore

/// Name, colour and icon for a new or existing project.
struct ProjectEditorView: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState
    @Environment(\.dismiss) private var dismiss

    let request: ProjectEditorRequest

    @State private var name = ""
    @State private var icon = "folder"
    @State private var color: ProjectColor = .blue
    @FocusState private var nameFocused: Bool

    private var isNew: Bool { request.projectID == nil }
    private var trimmedName: String { name.trimmingCharacters(in: .whitespacesAndNewlines) }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(isNew ? "New Project" : "Edit Project")
                .font(.system(size: 15, weight: .semibold))
                .foregroundColor(Theme.primaryText)

            HStack(spacing: 12) {
                Image(systemName: icon)
                    .font(.system(size: 17))
                    .foregroundColor(color.color)
                    .frame(width: 36, height: 36)
                    .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(Theme.raised))
                TextField("Project name", text: $name)
                    .textFieldStyle(.plain)
                    .font(.system(size: 16, weight: .medium))
                    .focused($nameFocused)
                    .onSubmit(save)
            }
            .padding(.top, 18)

            SectionHeader("Color").padding(.top, 22).padding(.bottom, 10)
            HStack(spacing: 10) {
                ForEach(ProjectColor.allCases) { option in
                    Button { color = option } label: {
                        Circle()
                            .fill(option.color)
                            .frame(width: 18, height: 18)
                            .padding(3)
                            .overlay(Circle().stroke(option == color ? Theme.primaryText : .clear, lineWidth: 1.5))
                    }
                    .buttonStyle(.plain)
                    .help(option.rawValue.capitalized)
                }
            }

            SectionHeader("Icon").padding(.top, 22).padding(.bottom, 10)
            LazyVGrid(columns: Array(repeating: GridItem(.fixed(34), spacing: 6), count: 8), alignment: .leading, spacing: 6) {
                ForEach(Project.iconChoices, id: \.self) { symbol in
                    Button { icon = symbol } label: {
                        Image(systemName: symbol)
                            .font(.system(size: 14))
                            .foregroundColor(symbol == icon ? color.color : Theme.secondaryText)
                            .frame(width: 34, height: 30)
                            .background(
                                RoundedRectangle(cornerRadius: 7, style: .continuous)
                                    .fill(symbol == icon ? Theme.selection : Color.clear)
                            )
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
            }

            HStack {
                if let projectID = request.projectID, let project = store.project(with: projectID) {
                    Button("Delete Project…", role: .destructive) {
                        dismiss()
                        state.projectPendingDeletion = project
                    }
                    .buttonStyle(.plain)
                    .foregroundColor(Theme.red)
                    .font(.system(size: 13))
                }
                Spacer()
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button(isNew ? "Create" : "Save", action: save)
                    .keyboardShortcut(.defaultAction)
                    .disabled(trimmedName.isEmpty)
            }
            .padding(.top, 26)
        }
        .padding(24)
        .frame(width: 400)
        .background(Theme.panel)
        .onAppear {
            if let project = store.project(with: request.projectID) {
                name = project.name
                icon = project.icon
                color = project.color
            }
            nameFocused = true
        }
    }

    private func save() {
        guard !trimmedName.isEmpty else { return }
        if let projectID = request.projectID {
            store.updateProject(projectID) { project in
                project.name = trimmedName
                project.icon = icon
                project.color = color
            }
        } else {
            let project = store.createProject(name: trimmedName, icon: icon, color: color)
            state.select(.project(project.id))
        }
        dismiss()
    }
}
