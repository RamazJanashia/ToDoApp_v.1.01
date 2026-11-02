import SwiftUI
import TodoCore

/// Sidebar · task list · inspector, under a seamless near-black title bar.
struct RootView: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState
    @Environment(\.undoManager) private var undoManager

    @AppStorage("sidebarWidth") private var sidebarWidth: Double = 250
    @AppStorage("inspectorWidth") private var inspectorWidth: Double = 340

    var body: some View {
        GeometryReader { proxy in
            let widths = columnWidths(for: proxy.size.width)
            HStack(spacing: 0) {
                if state.isSidebarVisible {
                    SidebarView()
                        .frame(width: widths.sidebar)
                        .transition(.move(edge: .leading).combined(with: .opacity))
                    ColumnResizer(
                        width: Binding(get: { widths.sidebar }, set: { sidebarWidth = $0 }),
                        range: Self.sidebarRange, direction: 1, showsLine: false
                    )
                }

                CenterView()
                    .frame(minWidth: Self.centerMinWidth, maxWidth: .infinity, maxHeight: .infinity)

                if state.isInspectorVisible {
                    ColumnResizer(
                        width: Binding(get: { widths.inspector }, set: { inspectorWidth = $0 }),
                        range: Self.inspectorRange, direction: -1
                    )
                    .padding(.vertical, 14)
                    InspectorView()
                        .frame(width: widths.inspector)
                        .transition(.move(edge: .trailing).combined(with: .opacity))
                }
            }
        }
        .frame(minWidth: minimumWidth, maxWidth: .infinity, minHeight: 600, maxHeight: .infinity)
        .background(Theme.background)
        .overlay(alignment: .top) { Hairline() }
        .toolbar { toolbarContent }
        .toolbarBackground(Theme.background, for: .windowToolbar)
        .toolbarBackground(.visible, for: .windowToolbar)
        .navigationTitle(state.windowTitle)
        .background(WindowConfigurator { window in state.mainWindow = window })
        .background(KeyboardMonitor { event in state.handleKeyDown(event) })
        .sheet(item: $state.projectEditor) { request in
            ProjectEditorView(request: request)
                .environmentObject(store)
                .environmentObject(state)
        }
        .alert(
            "Delete “\(state.projectPendingDeletion?.name ?? "")”?",
            isPresented: Binding(
                get: { state.projectPendingDeletion != nil },
                set: { if !$0 { state.projectPendingDeletion = nil } }
            )
        ) {
            Button("Delete Project", role: .destructive) {
                if let project = state.projectPendingDeletion { state.deleteProject(project) }
                state.projectPendingDeletion = nil
            }
            Button("Cancel", role: .cancel) { state.projectPendingDeletion = nil }
        } message: {
            Text(deletionMessage)
        }
        .alert(
            store.alert?.title ?? "",
            isPresented: Binding(get: { store.alert != nil }, set: { if !$0 { store.alert = nil } })
        ) {
            Button("OK") { store.alert = nil }
        } message: {
            Text(store.alert?.message ?? "")
        }
        .onAppear { store.undoManager = undoManager }
        .onChange(of: undoManager) { store.undoManager = $0 }
    }

    private var deletionMessage: String {
        let parent = store.project(with: state.projectPendingDeletion?.parentID)
        let home = parent.map { "“\($0.name)”" }
        let what = home.map { "Its tasks and sub-projects will move to \($0)." }
            ?? "Its tasks will stay in All Tasks without a project, and its sub-projects will move to the top level."
        return "\(what) You can undo this with ⌘Z."
    }

    private static let centerMinWidth: Double = 500
    private static let sidebarRange: ClosedRange<Double> = 200...340
    private static let inspectorRange: ClosedRange<Double> = 290...480

    /// The columns use the widths the user dragged them to. When that would leave the task list
    /// too little room, they give way down to their minimums, the inspector first.
    private func columnWidths(for total: Double) -> (sidebar: Double, inspector: Double) {
        var sidebar = min(max(sidebarWidth, Self.sidebarRange.lowerBound), Self.sidebarRange.upperBound)
        var inspector = min(max(inspectorWidth, Self.inspectorRange.lowerBound), Self.inspectorRange.upperBound)
        var excess = Self.centerMinWidth - total
            + (state.isSidebarVisible ? sidebar + 1 : 0)
            + (state.isInspectorVisible ? inspector + 1 : 0)
        if excess > 0, state.isInspectorVisible {
            let cut = min(excess, inspector - Self.inspectorRange.lowerBound)
            inspector -= cut
            excess -= cut
        }
        if excess > 0, state.isSidebarVisible {
            sidebar -= min(excess, sidebar - Self.sidebarRange.lowerBound)
        }
        return (sidebar, inspector)
    }

    /// The window can't shrink below what the visible columns need at their narrowest.
    private var minimumWidth: Double {
        (state.isSidebarVisible ? Self.sidebarRange.lowerBound + 1 : 0)
            + Self.centerMinWidth
            + (state.isInspectorVisible ? Self.inspectorRange.lowerBound + 1 : 0)
    }

    @ToolbarContentBuilder
    private var toolbarContent: some ToolbarContent {
        ToolbarItem(placement: .navigation) {
            IconButton(
                systemName: "sidebar.left",
                size: 15,
                help: state.isSidebarVisible ? "Hide Sidebar (⌃⌘S)" : "Show Sidebar (⌃⌘S)"
            ) {
                withAnimation(Theme.Motion.standard) { state.isSidebarVisible.toggle() }
            }
        }
        ToolbarItem(placement: .principal) {
            Text(state.windowTitle)
                .font(.system(size: 13, weight: .medium))
                .foregroundColor(Theme.primaryText.opacity(0.88))
                .lineLimit(1)
        }
        ToolbarItemGroup(placement: .automatic) {
            if state.shows(.toolbarSearch) {
                ToolbarSearchField()
            }
            if state.shows(.toolbarAppearance) {
                Menu {
                    Picker("Appearance", selection: $state.appearance) {
                        ForEach(AppearancePreference.allCases) { option in
                            Label(option.title, systemImage: option.icon).tag(option)
                        }
                    }
                    .pickerStyle(.inline)
                } label: {
                    Image(systemName: state.appearance.icon)
                }
                .menuIndicator(.hidden)
                .help("Appearance")
            }
            IconButton(
                systemName: "sidebar.right",
                size: 15,
                isActive: false,
                help: state.isInspectorVisible ? "Hide Inspector (⌥⌘I)" : "Show Inspector (⌥⌘I)"
            ) {
                withAnimation(Theme.Motion.standard) { state.isInspectorVisible.toggle() }
            }
        }
    }
}

/// A compact search field that lives in the title bar. ⌘F focuses it, Escape clears it.
struct ToolbarSearchField: View {
    @EnvironmentObject private var state: AppState
    @FocusState private var focused: Bool

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 11.5, weight: .medium))
                .foregroundColor(Theme.tertiaryText)
            TextField("Search", text: $state.searchText)
                .textFieldStyle(.plain)
                .font(.system(size: 13))
                .focused($focused)
                .onExitCommand {
                    state.searchText = ""
                    focused = false
                }
            if !state.searchText.isEmpty {
                Button {
                    state.searchText = ""
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 11))
                        .foregroundColor(Theme.tertiaryText)
                }
                .buttonStyle(.plain)
                .help("Clear Search")
            }
        }
        .padding(.horizontal, 9)
        .frame(width: 210, height: 28)
        .background(RoundedRectangle(cornerRadius: 7, style: .continuous).fill(Theme.panel))
        .overlay(
            RoundedRectangle(cornerRadius: 7, style: .continuous)
                .stroke(focused ? Theme.focusRing : Theme.separator, lineWidth: 1)
        )
        .onChange(of: state.searchFocusRequest) { _ in focused = true }
    }
}
