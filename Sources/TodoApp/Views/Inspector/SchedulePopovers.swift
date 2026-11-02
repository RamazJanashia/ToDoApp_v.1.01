import SwiftUI
import TodoCore

/// Quick picks, a month calendar, and an optional time of day.
struct DueDatePopover: View {
    @EnvironmentObject private var store: TaskStore

    let taskID: UUID

    private let calendar = Calendar.current

    private var task: TaskItem? { store.task(with: taskID) }

    private var dateBinding: Binding<Date> {
        Binding(
            get: { task?.dueDate ?? calendar.startOfDay(for: Date()) },
            set: { newValue in
                store.updateTask(taskID, undoName: "Set Due Date") { task in
                    if task.hasDueTime {
                        task.dueDate = newValue
                    } else {
                        task.dueDate = calendar.startOfDay(for: newValue)
                    }
                }
            }
        )
    }

    private var hasTimeBinding: Binding<Bool> {
        Binding(
            get: { task?.hasDueTime ?? false },
            set: { wantsTime in
                store.updateTask(taskID, undoName: wantsTime ? "Add Time" : "Remove Time") { task in
                    let day = calendar.startOfDay(for: task.dueDate ?? Date())
                    task.hasDueTime = wantsTime
                    task.dueDate = wantsTime
                        ? calendar.date(bySettingHour: 9, minute: 0, second: 0, of: day)
                        : day
                }
            }
        )
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 6) {
                QuickDateChip(title: "Today", icon: "sun.max") { setDay(offset: 0) }
                QuickDateChip(title: "Tomorrow", icon: "sunrise") { setDay(offset: 1) }
                QuickDateChip(title: "Next week", icon: "arrow.right.to.line") { setNextWeek() }
            }

            MiniMonthPicker(selection: task?.dueDate) { day in
                store.updateTask(taskID, undoName: "Set Due Date") { $0.reschedule(toDay: day) }
            }

            Hairline()

            HStack {
                Toggle("Time", isOn: hasTimeBinding)
                    .toggleStyle(.switch)
                    .controlSize(.small)
                    .font(.system(size: 13))
                    .disabled(task?.dueDate == nil)
                Spacer()
                if task?.hasDueTime == true {
                    DatePicker("Time", selection: dateBinding, displayedComponents: .hourAndMinute)
                        .labelsHidden()
                        .datePickerStyle(.field)
                        .frame(width: 90)
                }
            }

            if task?.dueDate != nil {
                OptionRow(icon: "xmark", title: "Remove due date") {
                    store.updateTask(taskID, undoName: "Remove Due Date") { $0.clearDueDate() }
                }
                .padding(.horizontal, -6)
            }
        }
        .padding(14)
        .frame(width: 270)
        .background(Theme.panel)
    }

    private func setDay(offset: Int) {
        let day = calendar.date(byAdding: .day, value: offset, to: calendar.startOfDay(for: Date())) ?? Date()
        store.updateTask(taskID, undoName: "Set Due Date") { $0.reschedule(toDay: day) }
    }

    private func setNextWeek() {
        let next = DueDateText.nextWeek(calendar: calendar)
        store.updateTask(taskID, undoName: "Set Due Date") { $0.reschedule(toDay: next) }
    }
}

/// A compact month calendar. Unlike the system graphical date picker it can show no
/// selection, so an undated task can be given any day — today included — in one click.
struct MiniMonthPicker: View {
    let selection: Date?
    let onSelect: (Date) -> Void

    @State private var month: Date
    private let calendar = Calendar.current

    init(selection: Date?, onSelect: @escaping (Date) -> Void) {
        self.selection = selection
        self.onSelect = onSelect
        _month = State(initialValue: selection ?? Date())
    }

    private var weekdaySymbols: [String] {
        let symbols = calendar.veryShortWeekdaySymbols
        let first = calendar.firstWeekday - 1
        return Array(symbols[first...] + symbols[..<first])
    }

    var body: some View {
        VStack(spacing: 6) {
            HStack(spacing: 2) {
                Text(month.formatted(.dateTime.month(.wide).year()))
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(Theme.primaryText)
                Spacer()
                IconButton(systemName: "chevron.left", size: 11, weight: .medium, help: "Previous Month") { shift(-1) }
                IconButton(systemName: "chevron.right", size: 11, weight: .medium, help: "Next Month") { shift(1) }
            }
            HStack(spacing: 0) {
                ForEach(Array(weekdaySymbols.enumerated()), id: \.offset) { _, symbol in
                    Text(symbol)
                        .font(.system(size: 10.5, weight: .medium))
                        .foregroundColor(Theme.tertiaryText)
                        .frame(maxWidth: .infinity)
                }
            }
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 0), count: 7), spacing: 2) {
                ForEach(MonthGrid.days(containing: month, calendar: calendar), id: \.self) { day in
                    MiniDayButton(
                        day: day,
                        isSelected: selection.map { calendar.isDate($0, inSameDayAs: day) } ?? false,
                        isToday: calendar.isDateInToday(day),
                        isInMonth: calendar.isDate(day, equalTo: month, toGranularity: .month)
                    ) { onSelect(day) }
                }
            }
        }
    }

    private func shift(_ months: Int) {
        month = calendar.date(byAdding: .month, value: months, to: month) ?? month
    }
}

private struct MiniDayButton: View {
    let day: Date
    let isSelected: Bool
    let isToday: Bool
    let isInMonth: Bool
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Text(day.formatted(.dateTime.day()))
                .font(.system(size: 12, weight: isToday || isSelected ? .semibold : .regular))
                .monospacedDigit()
                .foregroundColor(
                    isSelected ? Theme.background
                        : isToday ? Theme.red
                        : isInMonth ? Theme.primaryText : Theme.tertiaryText
                )
                .frame(width: 28, height: 26)
                .background(
                    Circle()
                        .fill(isSelected ? Theme.primaryText : (hovering ? Theme.selection : .clear))
                        .frame(width: 26, height: 26)
                )
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .help(day.formatted(date: .complete, time: .omitted))
    }
}

struct QuickDateChip: View {
    let title: String
    let icon: String
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            VStack(spacing: 4) {
                Image(systemName: icon).font(.system(size: 13))
                Text(title).font(.system(size: 11))
            }
            .foregroundColor(hovering ? Theme.primaryText : Theme.secondaryText)
            .frame(maxWidth: .infinity)
            .frame(height: 46)
            .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(hovering ? Theme.selection : Theme.raised))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }
}

struct ReminderPopover: View {
    @EnvironmentObject private var store: TaskStore

    let taskID: UUID
    let dismiss: () -> Void

    var body: some View {
        let task = store.task(with: taskID)
        PopoverSurface(width: 230) {
            if task?.dueDate == nil {
                Text("Set a due date first — reminders are sent relative to it.")
                    .font(Theme.Font.caption)
                    .foregroundColor(Theme.secondaryText)
                    .padding(10)
            } else {
                OptionRow(icon: "bell.slash", title: "None", isSelected: task?.reminderOffset == nil) {
                    store.updateTask(taskID, undoName: "Remove Reminder") { $0.reminderOffset = nil }
                    dismiss()
                }
                Hairline().padding(.vertical, 4)
                ForEach(ReminderPreset.offsets, id: \.self) { offset in
                    OptionRow(icon: nil, title: ReminderPreset.title(for: offset), isSelected: task?.reminderOffset == offset) {
                        store.updateTask(taskID, undoName: "Set Reminder") { $0.reminderOffset = offset }
                        store.confirmRemindersAllowed()
                        dismiss()
                    }
                }
                if task?.hasDueTime == false {
                    Text("All-day tasks are reminded relative to 9:00 AM.")
                        .font(Theme.Font.small)
                        .foregroundColor(Theme.tertiaryText)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 8)
                }
            }
        }
    }
}

/// Presets for the common cases plus a custom rule editor.
struct RepeatPopover: View {
    @EnvironmentObject private var store: TaskStore

    let taskID: UUID

    @State private var isCustomizing = false

    private var rule: RecurrenceRule? { store.task(with: taskID)?.recurrence }

    var body: some View {
        PopoverSurface(width: 260) {
            OptionRow(icon: "nosign", title: "Doesn’t repeat", isSelected: rule == nil) {
                apply(nil)
            }
            Hairline().padding(.vertical, 4)
            ForEach(RecurrenceRule.presets, id: \.title) { preset in
                OptionRow(icon: nil, title: preset.title, isSelected: rule == preset.rule) {
                    apply(preset.rule)
                }
            }
            Hairline().padding(.vertical, 4)
            OptionRow(icon: "slider.horizontal.3", title: "Custom…", isSelected: isCustomRule) {
                withAnimation(Theme.Motion.quick) { isCustomizing.toggle() }
                if rule == nil { apply(.weekly) }
            }
            if isCustomizing || isCustomRule {
                CustomRecurrenceEditor(rule: rule ?? .weekly) { apply($0) }
                    .padding(.horizontal, 10)
                    .padding(.top, 8)
                    .padding(.bottom, 6)
            }
        }
    }

    private var isCustomRule: Bool {
        guard let rule else { return false }
        return !RecurrenceRule.presets.contains { $0.rule == rule }
    }

    private func apply(_ rule: RecurrenceRule?) {
        store.updateTask(taskID, undoName: rule == nil ? "Stop Repeating" : "Set Repeat") { task in
            task.recurrence = rule
            // A repeating task needs an anchor date to count from.
            if rule != nil && task.dueDate == nil {
                task.dueDate = Calendar.current.startOfDay(for: Date())
            }
        }
    }
}

struct CustomRecurrenceEditor: View {
    let rule: RecurrenceRule
    let onChange: (RecurrenceRule) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Picker("Frequency", selection: Binding(
                get: { rule.frequency },
                set: { onChange(RecurrenceRule(frequency: $0, interval: rule.interval, weekdays: $0 == .weekly ? rule.weekdays : [])) }
            )) {
                ForEach(RecurrenceRule.Frequency.allCases) { frequency in
                    Text(frequency.title).tag(frequency)
                }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .controlSize(.small)

            HStack(spacing: 8) {
                Text("Every")
                Stepper(
                    value: Binding(
                        get: { rule.interval },
                        set: { onChange(RecurrenceRule(frequency: rule.frequency, interval: $0, weekdays: rule.weekdays)) }
                    ),
                    in: 1...99
                ) {
                    Text("\(rule.interval) \(rule.frequency.unit(plural: rule.interval != 1))")
                        .monospacedDigit()
                        .foregroundColor(Theme.primaryText)
                }
                .controlSize(.small)
            }
            .font(.system(size: 13))
            .foregroundColor(Theme.secondaryText)

            if rule.frequency == .weekly {
                WeekdayPicker(selection: rule.weekdays) { weekdays in
                    onChange(RecurrenceRule(frequency: .weekly, interval: rule.interval, weekdays: weekdays))
                }
            }

            Text(rule.summary)
                .font(Theme.Font.small)
                .foregroundColor(Theme.tertiaryText)
        }
    }
}

struct WeekdayPicker: View {
    let selection: [Int]
    let onChange: ([Int]) -> Void

    private var orderedWeekdays: [Int] {
        let first = Calendar.current.firstWeekday
        return (0..<7).map { (first - 1 + $0) % 7 + 1 }
    }

    var body: some View {
        HStack(spacing: 5) {
            ForEach(orderedWeekdays, id: \.self) { weekday in
                let isOn = selection.contains(weekday)
                Button {
                    var updated = Set(selection)
                    if isOn { updated.remove(weekday) } else { updated.insert(weekday) }
                    onChange(Array(updated))
                } label: {
                    Text(Calendar.current.veryShortWeekdaySymbols[weekday - 1])
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(isOn ? Theme.background : Theme.secondaryText)
                        .frame(width: 25, height: 25)
                        .background(Circle().fill(isOn ? Theme.primaryText : Theme.raised))
                        .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .help(Calendar.current.weekdaySymbols[weekday - 1])
            }
        }
    }
}
