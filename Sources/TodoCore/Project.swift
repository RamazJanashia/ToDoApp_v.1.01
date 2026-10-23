import Foundation

public struct Project: Identifiable, Codable, Hashable, Sendable {
    public var id: UUID
    public var name: String
    /// An SF Symbol name.
    public var icon: String
    public var color: ProjectColor

    public init(id: UUID = UUID(), name: String, icon: String = "folder", color: ProjectColor = .blue) {
        self.id = id
        self.name = name
        self.icon = icon
        self.color = color
    }

    /// Symbols offered in the project editor.
    public static let iconChoices: [String] = [
        "folder", "briefcase", "house", "person", "heart", "star",
        "book", "cart", "figure.run", "lightbulb", "hammer", "airplane",
        "graduationcap", "leaf", "music.note", "gamecontroller", "paintbrush", "laptopcomputer",
        "chart.bar", "dollarsign.circle", "camera", "gift", "tray.full", "sparkles"
    ]
}

/// A restrained palette; values tuned to sit quietly on a near-black background.
public enum ProjectColor: String, Codable, CaseIterable, Identifiable, Sendable {
    case blue, green, red, orange, yellow, purple, pink, teal, graphite, white

    public var id: String { rawValue }

    public var hex: UInt32 {
        switch self {
        case .blue: return 0x4C8DF6
        case .green: return 0x4CC38A
        case .red: return 0xEF5B53
        case .orange: return 0xF2994A
        case .yellow: return 0xF2C94C
        case .purple: return 0xA78BFA
        case .pink: return 0xF472B6
        case .teal: return 0x4FD1C5
        case .graphite: return 0x8E8E93
        case .white: return 0xE6E6EA
        }
    }
}
