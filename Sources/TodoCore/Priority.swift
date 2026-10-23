import Foundation

public enum Priority: Int, Codable, CaseIterable, Identifiable, Comparable, Sendable {
    case none = 0
    case low = 1
    case medium = 2
    case high = 3

    public var id: Int { rawValue }

    public var title: String {
        switch self {
        case .none: return "No Priority"
        case .low: return "Low"
        case .medium: return "Medium"
        case .high: return "High"
        }
    }

    /// Highest first, the order menus and pickers show them in.
    public static let descending: [Priority] = [.high, .medium, .low, .none]

    public static func < (lhs: Priority, rhs: Priority) -> Bool {
        lhs.rawValue < rhs.rawValue
    }
}

public enum TaskStatus: String, Codable, CaseIterable, Identifiable, Sendable {
    case todo
    case inProgress
    case done

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .todo: return "To Do"
        case .inProgress: return "In Progress"
        case .done: return "Done"
        }
    }
}
