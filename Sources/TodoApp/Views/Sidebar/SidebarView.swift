import SwiftUI
import TodoCore

struct SidebarView: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState

    @State private var hoveringProjects = false

    var body: some View {
        let now = Date()
        ScrollView {
            VStack(alignment: .leading, spacing: 4) {
                SectionHeader("Workspace")
                    .padding(.horizontal, 10)
                    .padding(.bottom, 8)

                SidebarRow(item: .inbox, icon: "tray", title: "Inbox", count: count(.inbox, now))
                SidebarRow(item: .today, icon: "sun.max", title: "Today", count: count(.today, now))
                SidebarRow(item: .upcoming, icon: "calendar", title: "Upcoming", count: count(.upcoming, now))
                SidebarRow(item: .completed, icon: "checkmark.circle", title: "Completed")
                SidebarRow(item: .dashboard, icon: "square.grid.2x2", title: "Dashboard")

                HStack {
                    SectionHeader("Projects")
                    Spacer()
                    IconButton(systemName: "plus", size: 12, weight: .medium, help: "New Project (⇧⌘N)") {
                        state.projectEditor = ProjectEditorRequest(projectID: nil)
                    }
                    .opacity(hoveringProjects || store.projects.isEmpty ? 1 : 0)
                }
                .padding(.leading, 10)
                .padding(.trailing, 2)
                .padding(.top, 26)
                .padding(.bottom, 4)

                ForEach(store.projects) { project in
                    SidebarRow(
                        item: .project(project.id),
                        icon: project.icon,
                        tint: project.color.color,
                        title: project.name,
                        count: count(.project(project.id), now)
                    )
                    .draggable("project:\(project.id.uuidString)")
                    .contextMenu {
                        Button("Edit Project…") { state.projectEditor = ProjectEditorRequest(projectID: project.id) }
                        Divider()
                        Button("Delete Project…", role: .destructive) { state.projectPendingDeletion = project }
                    }
                }

                if store.projects.isEmpty {
                    Text("Create a project to group related tasks.")
                        .font(Theme.Font.caption)
                        .foregroundColor(Theme.tertiaryText)
                        .padding(.horizontal, 10)
                        .padding(.top, 4)
                }
            }
            .padding(.horizontal, 12)
            .padding(.top, 20)
            .padding(.bottom, 20)
            .onHover { hoveringProjects = $0 }
        }
        .scrollIndicators(.never)
        .background(Theme.background)
    }

    private func count(_ scope: TaskScope, _ now: Date) -> Int {
        TaskQuery.openCount(in: scope, tasks: store.tasks, now: now)
    }
}

struct SidebarRow: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState

    let item: SidebarItem
    let icon: String
    var tint: Color?
    let title: String
    var count: Int?

    @State private var hovering = false
    @State private var isDropTarget = false

    private var isSelected: Bool { state.sidebarSelection == item && !state.isSearching }

    var body: some View {
        HStack(spacing: 11) {
            Image(systemName: icon)
                .font(.system(size: 14, weight: .regular))
                .foregroundColor(tint ?? Theme.primaryText.opacity(0.82))
                .frame(width: 20)
            Text(title)
                .font(Theme.Font.body)
                .foregroundColor(Theme.primaryText)
                .lineLimit(1)
            Spacer(minLength: 8)
            if let count, count > 0 {
                Text("\(count)")
                    .font(.system(size: 13))
                    .monospacedDigit()
                    .foregroundColor(Theme.secondaryText)
            }
        }
        .padding(.horizontal, 10)
        .frame(height: 38)
        .rowHighlight(selected: isSelected, hovering: hovering)
        .overlay(
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .stroke(isDropTarget ? Theme.blue.opacity(0.75) : .clear, lineWidth: 1.5)
        )
        .contentShape(Rectangle())
        .onTapGesture { state.select(item) }
        .onHover { hovering = $0 }
        .dropDestination(for: String.self) { items, _ in
            if case .project(let targetID) = item,
               let projectID = items.first.flatMap(Self.projectID(from:)) {
                store.moveProject(projectID, before: targetID)
                return true
            }
            return state.handleDrop(items, on: item)
        } isTargeted: { isDropTarget = $0 && item != .dashboard }
        .animation(Theme.Motion.quick, value: isSelected)
    }

    private static func projectID(from item: String) -> UUID? {
        guard item.hasPrefix("project:") else { return nil }
        return UUID(uuidString: String(item.dropFirst("project:".count)))
    }
}
