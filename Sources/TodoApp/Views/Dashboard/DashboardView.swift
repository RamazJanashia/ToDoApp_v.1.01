import Charts
import SwiftUI
import TodoCore

/// An at-a-glance overview: today's load, recent momentum, and where each project stands.
struct DashboardView: View {
    @EnvironmentObject private var store: TaskStore
    @EnvironmentObject private var state: AppState

    var body: some View {
        let stats = DashboardStats(tasks: store.tasks, projects: store.projects.outline().map(\.project))

        GeometryReader { proxy in
            // The width the content actually gets, which picks the layout below.
            let width = min(proxy.size.width - Theme.Layout.pageMargin * 2, Theme.Layout.dashboardWidth)
            ScrollView {
                content(stats, width: width)
                    .frame(maxWidth: Theme.Layout.dashboardWidth)
                    .padding(.horizontal, Theme.Layout.pageMargin)
                    .frame(maxWidth: .infinity)
                    .padding(.top, 26)
                    .padding(.bottom, 48)
            }
            .scrollIndicators(.automatic)
        }
        .background(Theme.background)
    }

    private func content(_ stats: DashboardStats, width: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("Dashboard")
                .font(Theme.Font.pageTitle)
                .foregroundColor(Theme.primaryText)
            Text("\(Date().formatted(.dateTime.weekday(.wide).month(.wide).day())) · \(stats.open) open tasks")
                .font(Theme.Font.body)
                .foregroundColor(Theme.secondaryText)
                .padding(.top, 10)
            if state.shows(.dashboardProgress) {
                ProgressLine(fraction: stats.completionRate)
                    .padding(.top, 16)
                Text("\(Int((stats.completionRate * 100).rounded()))% of all tasks complete")
                    .font(Theme.Font.caption)
                    .foregroundColor(Theme.tertiaryText)
                    .padding(.top, 8)
            }

            if state.shows(.dashboardMetrics) {
                metrics(stats, inOneRow: width >= 600)
                    .padding(.top, 28)
            }

            if state.shows(.dashboardActivity) {
                VStack(alignment: .leading, spacing: 4) {
                    SectionHeader("Activity")
                    Text("Tasks completed in the last 14 days")
                        .font(Theme.Font.caption)
                        .foregroundColor(Theme.tertiaryText)
                }
                .padding(.top, 34)
                ActivityChart(days: stats.activity)
                    .frame(height: 170)
                    .padding(.top, 16)
            }

            if width >= 680 {
                HStack(alignment: .top, spacing: 44) {
                    if state.shows(.dashboardProjects) {
                        projectsSection(stats).frame(maxWidth: .infinity, alignment: .topLeading)
                    }
                    if state.shows(.dashboardUpNext) {
                        upNextSection(stats).frame(maxWidth: .infinity, alignment: .topLeading)
                    }
                }
            } else {
                if state.shows(.dashboardProjects) { projectsSection(stats) }
                if state.shows(.dashboardUpNext) { upNextSection(stats) }
            }

            if state.shows(.dashboardPriority) {
                prioritySection(stats)
            }
        }
    }

    /// Four tiles in a row when there's room, otherwise a 2 × 2 grid — never squeezed labels.
    @ViewBuilder
    private func metrics(_ stats: DashboardStats, inOneRow: Bool) -> some View {
        let dueToday = MetricTile(value: stats.dueToday, label: "Due today") { state.select(.today) }
        let overdue = MetricTile(value: stats.overdue, label: "Overdue", tint: stats.overdue > 0 ? Theme.red : nil) { state.select(.today) }
        let completedToday = MetricTile(value: stats.completedToday, label: "Completed today") { state.select(.completed) }
        let thisWeek = MetricTile(value: stats.completedThisWeek, label: "Done this week") { state.select(.completed) }

        if inOneRow {
            HStack(spacing: 0) {
                dueToday
                VerticalHairline().padding(.vertical, 18)
                overdue
                VerticalHairline().padding(.vertical, 18)
                completedToday
                VerticalHairline().padding(.vertical, 18)
                thisWeek
            }
            .frame(height: 96)
            .overlay(alignment: .top) { Hairline() }
            .overlay(alignment: .bottom) { Hairline() }
        } else {
            VStack(spacing: 0) {
                HStack(spacing: 0) {
                    dueToday
                    VerticalHairline().padding(.vertical, 16)
                    overdue
                }
                .frame(height: 88)
                Hairline()
                HStack(spacing: 0) {
                    completedToday
                    VerticalHairline().padding(.vertical, 16)
                    thisWeek
                }
                .frame(height: 88)
            }
            .overlay(alignment: .top) { Hairline() }
            .overlay(alignment: .bottom) { Hairline() }
        }
    }

    private func projectsSection(_ stats: DashboardStats) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            SectionHeader("Projects")
                .padding(.top, 36)
                .padding(.bottom, 8)
            if stats.projects.isEmpty {
                Text("No projects yet.")
                    .font(Theme.Font.caption)
                    .foregroundColor(Theme.tertiaryText)
                    .padding(.vertical, 12)
            }
            ForEach(stats.projects) { entry in
                ProjectProgressRow(entry: entry) { state.select(.project(entry.project.id)) }
            }
        }
    }

    private func upNextSection(_ stats: DashboardStats) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            SectionHeader("Up Next")
                .padding(.top, 36)
                .padding(.bottom, 8)
            if stats.upNext.isEmpty {
                Text("Nothing scheduled.")
                    .font(Theme.Font.caption)
                    .foregroundColor(Theme.tertiaryText)
                    .padding(.vertical, 12)
            }
            ForEach(stats.upNext) { task in
                UpNextRow(task: task) { state.reveal(task.id) }
            }
        }
    }

    private func prioritySection(_ stats: DashboardStats) -> some View {
        let levels: [Priority] = [.high, .medium, .low]
        let largest = max(levels.map { stats.openByPriority[$0] ?? 0 }.max() ?? 0, 1)
        return VStack(alignment: .leading, spacing: 0) {
            SectionHeader("Open by Priority")
                .padding(.top, 36)
                .padding(.bottom, 12)
            ForEach(levels) { priority in
                let count = stats.openByPriority[priority] ?? 0
                HStack(spacing: 12) {
                    Image(systemName: "flag")
                        .font(.system(size: 12))
                        .foregroundColor(priority.tint)
                        .frame(width: 16)
                    Text(priority.title)
                        .font(Theme.Font.body)
                        .foregroundColor(Theme.primaryText)
                        .frame(width: 70, alignment: .leading)
                    GeometryReader { proxy in
                        Capsule()
                            .fill(priority.tint)
                            .frame(width: max(count > 0 ? 4 : 0, proxy.size.width * Double(count) / Double(largest)))
                            .frame(maxHeight: .infinity)
                    }
                    .frame(height: 4)
                    Text("\(count)")
                        .font(Theme.Font.caption)
                        .monospacedDigit()
                        .foregroundColor(Theme.secondaryText)
                        .frame(width: 28, alignment: .trailing)
                }
                .frame(height: 30)
            }
        }
        .frame(maxWidth: 520, alignment: .leading)
    }
}

struct MetricTile: View {
    let value: Int
    let label: String
    var tint: Color?
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 6) {
                Text("\(value)")
                    .font(.system(size: 30, weight: .light))
                    .monospacedDigit()
                    .foregroundColor(tint ?? Theme.primaryText)
                Text(label)
                    .font(Theme.Font.caption)
                    .foregroundColor(hovering ? Theme.primaryText : Theme.secondaryText)
                    .lineLimit(1)
            }
            // Top-aligned so the numbers line up even if a label is longer.
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .padding(.leading, 18)
            .padding(.top, 18)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }
}

struct ProjectProgressRow: View {
    let entry: DashboardStats.ProjectProgress
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 10) {
                    Image(systemName: entry.project.icon)
                        .font(.system(size: 13))
                        .foregroundColor(entry.project.color.color)
                        .frame(width: 18)
                    Text(entry.project.name)
                        .font(Theme.Font.body)
                        .foregroundColor(Theme.primaryText)
                    Spacer()
                    Text("\(entry.done) of \(entry.total)")
                        .font(Theme.Font.caption)
                        .monospacedDigit()
                        .foregroundColor(Theme.secondaryText)
                }
                GeometryReader { proxy in
                    ZStack(alignment: .leading) {
                        Capsule().fill(Theme.track)
                        Capsule()
                            .fill(entry.project.color.color.opacity(0.85))
                            .frame(width: max(entry.done > 0 ? 4 : 0, proxy.size.width * entry.fraction))
                    }
                }
                .frame(height: 3)
                .padding(.leading, 28)
            }
            .padding(.vertical, 11)
            .padding(.horizontal, 8)
            .rowHighlight(selected: false, hovering: hovering)
            .padding(.horizontal, -8)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }
}

struct UpNextRow: View {
    @EnvironmentObject private var store: TaskStore

    let task: TaskItem
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 12) {
                Circle().fill(Theme.checkboxFill).frame(width: 9, height: 9)
                VStack(alignment: .leading, spacing: 3) {
                    Text(task.title.isEmpty ? "Untitled" : task.title)
                        .font(Theme.Font.body)
                        .foregroundColor(Theme.primaryText)
                        .lineLimit(1)
                    if let project = store.project(with: task.projectID) {
                        Text(project.name)
                            .font(Theme.Font.caption)
                            .foregroundColor(Theme.secondaryText)
                    }
                }
                Spacer(minLength: 12)
                if let due = task.dueDate {
                    Text(DueDateText.label(for: due, hasTime: task.hasDueTime))
                        .font(Theme.Font.caption)
                        .foregroundColor(task.isOverdue() ? Theme.red : Theme.secondaryText)
                }
                PriorityFlag(priority: task.priority)
            }
            .padding(.vertical, 9)
            .padding(.horizontal, 8)
            .rowHighlight(selected: false, hovering: hovering)
            .padding(.horizontal, -8)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }
}

/// Completions per day: one quiet series, today and the hovered day brought forward.
struct ActivityChart: View {
    let days: [DashboardStats.DayCount]

    @State private var hoveredDay: Date?

    private var peak: Int { days.map(\.count).max() ?? 0 }

    var body: some View {
        Chart(days) { item in
            BarMark(
                x: .value("Day", item.day, unit: .day),
                y: .value("Completed", item.count),
                width: .ratio(0.5)
            )
            .foregroundStyle(color(for: item))
            .cornerRadius(4)
            .annotation(position: .top, alignment: .center, spacing: 6) {
                if isHovered(item) {
                    VStack(spacing: 2) {
                        Text("\(item.count) completed")
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundColor(Theme.primaryText)
                        Text(item.day.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day()))
                            .font(.system(size: 10.5))
                            .foregroundColor(Theme.secondaryText)
                    }
                    .padding(.horizontal, 8)
                    .padding(.vertical, 5)
                    .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Theme.raised))
                    .overlay(RoundedRectangle(cornerRadius: 6, style: .continuous).stroke(Theme.separator))
                }
            }
        }
        .chartYScale(domain: 0...max(peak + 2, 4))
        .chartXAxis {
            AxisMarks(values: .stride(by: .day, count: 2)) { _ in
                AxisValueLabel(format: .dateTime.day(), centered: true)
                    .foregroundStyle(Theme.tertiaryText)
            }
        }
        .chartYAxis {
            AxisMarks(position: .leading, values: .automatic(desiredCount: 3)) { _ in
                AxisGridLine().foregroundStyle(Theme.separator)
                AxisValueLabel().foregroundStyle(Theme.tertiaryText)
            }
        }
        .chartOverlay { proxy in
            GeometryReader { geometry in
                Rectangle()
                    .fill(Color.clear)
                    .contentShape(Rectangle())
                    .onContinuousHover { phase in
                        switch phase {
                        case .active(let location):
                            let x = location.x - geometry[proxy.plotAreaFrame].origin.x
                            hoveredDay = proxy.value(atX: x, as: Date.self)
                        case .ended:
                            hoveredDay = nil
                        }
                    }
            }
        }
        .accessibilityLabel("Tasks completed per day over the last 14 days")
    }

    private func isHovered(_ item: DashboardStats.DayCount) -> Bool {
        hoveredDay.map { Calendar.current.isDate($0, inSameDayAs: item.day) } ?? false
    }

    private func color(for item: DashboardStats.DayCount) -> Color {
        if isHovered(item) { return Theme.chartHover }
        if Calendar.current.isDateInToday(item.day) { return Theme.chartToday }
        return Theme.chartBar
    }
}
