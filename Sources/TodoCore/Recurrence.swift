import Foundation

/// How a task repeats: every `interval` days, weeks, months or years.
public struct RecurrenceRule: Codable, Hashable, Sendable {
    public enum Frequency: String, Codable, CaseIterable, Identifiable, Sendable {
        case daily
        case weekly
        case monthly
        case yearly

        public var id: String { rawValue }

        public var title: String {
            switch self {
            case .daily: return "Daily"
            case .weekly: return "Weekly"
            case .monthly: return "Monthly"
            case .yearly: return "Yearly"
            }
        }

        public func unit(plural: Bool) -> String {
            switch self {
            case .daily: return plural ? "days" : "day"
            case .weekly: return plural ? "weeks" : "week"
            case .monthly: return plural ? "months" : "month"
            case .yearly: return plural ? "years" : "year"
            }
        }
    }

    public var frequency: Frequency
    public var interval: Int
    /// Weekdays for weekly rules (1 = Sunday … 7 = Saturday).
    /// Empty means "the same weekday as the due date".
    public var weekdays: [Int]

    public init(frequency: Frequency, interval: Int = 1, weekdays: [Int] = []) {
        self.frequency = frequency
        self.interval = max(1, interval)
        self.weekdays = Array(Set(weekdays.filter { (1...7).contains($0) })).sorted()
    }

    public static let daily = RecurrenceRule(frequency: .daily)
    public static let weekdays = RecurrenceRule(frequency: .weekly, weekdays: [2, 3, 4, 5, 6])
    public static let weekly = RecurrenceRule(frequency: .weekly)
    public static let monthly = RecurrenceRule(frequency: .monthly)
    public static let yearly = RecurrenceRule(frequency: .yearly)

    /// The ready-made rules offered before "Custom…".
    public static let presets: [(title: String, rule: RecurrenceRule)] = [
        ("Every Day", .daily),
        ("Every Weekday", .weekdays),
        ("Every Week", .weekly),
        ("Every Month", .monthly),
        ("Every Year", .yearly)
    ]

    /// The first occurrence strictly after `date`.
    ///
    /// For monthly and yearly rules, `preferredDay` is the day of the month the series aims
    /// for. Without it, a series that passes through a short month (Jan 31 → Feb 28) would
    /// stay on the 28th for good.
    public func nextDate(after date: Date, preferredDay: Int? = nil, calendar: Calendar = .current) -> Date {
        let step = max(1, interval)
        switch frequency {
        case .daily:
            return calendar.date(byAdding: .day, value: step, to: date) ?? date
        case .monthly:
            return Self.setting(day: preferredDay, in: calendar.date(byAdding: .month, value: step, to: date) ?? date, calendar: calendar)
        case .yearly:
            return Self.setting(day: preferredDay, in: calendar.date(byAdding: .year, value: step, to: date) ?? date, calendar: calendar)
        case .weekly:
            guard !weekdays.isEmpty else {
                return calendar.date(byAdding: .weekOfYear, value: step, to: date) ?? date
            }
            let startWeek = startOfWeek(for: date, calendar: calendar)
            for offset in 1...(7 * step + 7) {
                guard let candidate = calendar.date(byAdding: .day, value: offset, to: date) else { continue }
                guard weekdays.contains(calendar.component(.weekday, from: candidate)) else { continue }
                let candidateWeek = startOfWeek(for: candidate, calendar: calendar)
                let days = calendar.dateComponents([.day], from: startWeek, to: candidateWeek).day ?? 0
                let weeks = Int((Double(days) / 7).rounded())
                if weeks % step == 0 { return candidate }
            }
            return calendar.date(byAdding: .weekOfYear, value: step, to: date) ?? date
        }
    }

    public var summary: String {
        switch frequency {
        case .weekly:
            if interval == 1 && weekdays == [2, 3, 4, 5, 6] { return "Every weekday" }
            let base = interval == 1 ? "Weekly" : "Every \(interval) weeks"
            guard !weekdays.isEmpty else { return base }
            let symbols = Calendar.current.shortWeekdaySymbols
            let names = weekdays.map { symbols[$0 - 1] }
            return "\(base) on \(names.joined(separator: ", "))"
        default:
            if interval == 1 { return frequency.title }
            return "Every \(interval) \(frequency.unit(plural: true))"
        }
    }

    /// Moves `date` to `day` of its month, or the month's last day when the month is shorter.
    private static func setting(day: Int?, in date: Date, calendar: Calendar) -> Date {
        guard let day, let length = calendar.range(of: .day, in: .month, for: date)?.count else { return date }
        var components = calendar.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
        components.day = min(day, length)
        return calendar.date(from: components) ?? date
    }

    private func startOfWeek(for date: Date, calendar: Calendar) -> Date {
        calendar.dateInterval(of: .weekOfYear, for: date)?.start ?? calendar.startOfDay(for: date)
    }
}
