import SwiftUI
import TodoCore

struct SidebarView: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState

    @State private var hoveringProjects = false

    private var showsWorkspace: Bool {
        WorkspaceElement.Group.sidebar.elements.contains { $0 != .projects && state.shows($0) }
    }

    var body: some View {
        let now = Date()
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 4) {
                    if showsWorkspace {
                        SectionHeader("Workspace")
                            .padding(.horizontal, 10)
                            .padding(.bottom, 8)
                            .contextMenu { customizeButton }
                    }

                    place(.allTasks, icon: "tray", title: "All Tasks", count: count(.allOpen, now))
                    place(.today, icon: "sun.max", title: "Today", count: count(.today, now))
                    place(.upcoming, icon: "calendar", title: "Upcoming", count: count(.upcoming, now))
                    place(.completed, icon: "checkmark.circle", title: "Completed")
                    place(.dashboard, icon: "square.grid.2x2", title: "Dashboard")

                    if state.shows(.projects) {
                        projectsHeader
                        ForEach(store.projects.outline(collapsed: state.collapsedProjects)) { row in
                            projectRow(row, now: now)
                        }
                        if store.projects.isEmpty {
                            Text("Create a project to group related tasks.")
                                .font(Theme.Font.caption)
                                .foregroundColor(Theme.tertiaryText)
                                .padding(.horizontal, 10)
                                .padding(.top, 4)
                        }
                    }
                }
                .padding(.horizontal, 12)
                .padding(.top, 20)
                .padding(.bottom, 20)
                .onHover { hoveringProjects = $0 }
            }
            .scrollIndicators(.never)
            CustomizeButton()
                .padding(.horizontal, 12)
                .padding(.vertical, 10)
        }
        .background(Theme.background)
    }

    @ViewBuilder
    private func place(_ item: SidebarItem, icon: String, title: String, count: Int? = nil) -> some View {
        if state.shows(item.element) {
            SidebarRow(item: item, icon: icon, title: title, count: count)
                .contextMenu {
                    Button("Hide \(title)") { state.setVisible(false, item.element) }
                    customizeButton
                }
        }
    }

    private var customizeButton: some View {
        Button("Customize…") { state.openSettings() }
    }

    private var projectsHeader: some View {
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
        .padding(.top, showsWorkspace ? 26 : 0)
        .padding(.bottom, 4)
        .contextMenu {
            Button("Hide Projects") { state.setVisible(false, .projects) }
            customizeButton
        }
    }

    private func projectRow(_ row: ProjectOutlineRow, now: Date) -> some View {
        let project = row.project
        return SidebarRow(
            item: .project(project.id),
            icon: project.icon,
            tint: project.color.color,
            title: project.name,
            count: count(.projects(store.projects.subtreeIDs(of: project.id)), now),
            depth: row.depth,
            isCollapsed: row.hasChildren ? state.collapsedProjects.contains(project.id) : nil
        )
        .draggable("project:\(project.id.uuidString)")
        .contextMenu {
            Button("New Sub-project…") {
                state.projectEditor = ProjectEditorRequest(projectID: nil, parentID: project.id)
            }
            Button("Edit Project…") { state.projectEditor = ProjectEditorRequest(projectID: project.id) }
            Divider()
            Button("Delete Project…", role: .destructive) { state.projectPendingDeletion = project }
        }
    }

    private func count(_ scope: TaskScope, _ now: Date) -> Int {
        TaskQuery.openCount(in: scope, tasks: store.tasks, now: now)
    }
}

/// Always in view at the foot of the sidebar, so hidden things can always be brought back.
struct CustomizeButton: View {
    @EnvironmentObject private var state: AppState

    @State private var hovering = false

    var body: some View {
        Button {
            state.openSettings()
        } label: {
            HStack(spacing: 11) {
                Image(systemName: "slider.horizontal.3")
                    .font(.system(size: 14))
                    .frame(width: 20)
                Text("Customize")
                    .font(Theme.Font.body)
                Spacer(minLength: 0)
            }
            .foregroundColor(hovering ? Theme.primaryText : Theme.secondaryText)
            .padding(.horizontal, 10)
            .frame(height: 34)
            .rowHighlight(selected: false, hovering: hovering)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .help("Choose what the app shows (⌘,)")
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
    /// How many levels this project sits under others.
    var depth = 0
    /// `nil` for a row with nothing under it.
    var isCollapsed: Bool?

    @State private var hovering = false
    @State private var isDropTarget = false

    private var isSelected: Bool { state.sidebarSelection == item && !state.isSearching }

    private var acceptsDrops: Bool { item != .dashboard && item != .allTasks }

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
            if let isCollapsed, case .project(let id) = item {
                Button {
                    withAnimation(Theme.Motion.quick) { state.toggleCollapsed(id) }
                } label: {
                    Image(systemName: isCollapsed ? "chevron.right" : "chevron.down")
                        .font(.system(size: 9.5, weight: .semibold))
                        .foregroundColor(Theme.tertiaryText)
                        .frame(width: 16, height: 20)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help(isCollapsed ? "Show Sub-projects" : "Hide Sub-projects")
            }
        }
        .padding(.leading, 10 + CGFloat(depth) * 16)
        .padding(.trailing, 10)
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
        } isTargeted: { isDropTarget = $0 && acceptsDrops }
        .accessibleRow(title, isSelected: isSelected) { state.select(item) }
        .animation(Theme.Motion.quick, value: isSelected)
    }

    private static func projectID(from item: String) -> UUID? {
        guard item.hasPrefix("project:") else { return nil }
        return UUID(uuidString: String(item.dropFirst("project:".count)))
    }
}
