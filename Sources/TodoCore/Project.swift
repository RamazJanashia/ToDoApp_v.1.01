import Foundation

public struct Project: Identifiable, Codable, Hashable, Sendable {
    public var id: UUID
    public var name: String
    /// An SF Symbol name.
    public var icon: String
    public var color: ProjectColor
    /// The project this one sits under; `nil` for a top-level project.
    public var parentID: UUID?

    public init(id: UUID = UUID(), name: String, icon: String = "folder", color: ProjectColor = .blue, parentID: UUID? = nil) {
        self.id = id
        self.name = name
        self.icon = icon
        self.color = color
        self.parentID = parentID
    }

    /// Symbols offered in the project editor.
    public static let iconChoices: [String] = [
        "folder", "briefcase", "house", "person", "heart", "star",
        "book", "cart", "figure.run", "lightbulb", "hammer", "airplane",
        "graduationcap", "leaf", "music.note", "gamecontroller", "paintbrush", "laptopcomputer",
        "chart.bar", "dollarsign.circle", "camera", "gift", "tray.full", "sparkles"
    ]
}

/// One line of the project tree, in display order.
public struct ProjectOutlineRow: Identifiable, Hashable, Sendable {
    public var project: Project
    public var depth: Int
    public var hasChildren: Bool
    public var id: UUID { project.id }
}

public extension Array where Element == Project {
    /// Every project once, parents directly above their children. A project whose parent is
    /// missing, or that sits in a loop (a hand-edited file), still appears, at the top level.
    func outline() -> [ProjectOutlineRow] {
        let ids = Set(map(\.id))
        var children: [UUID: [Project]] = [:]
        for project in self {
            if let parent = project.parentID, ids.contains(parent) { children[parent, default: []].append(project) }
        }
        var rows: [ProjectOutlineRow] = []
        var visited: Set<UUID> = []
        func visit(_ project: Project, depth: Int) {
            guard visited.insert(project.id).inserted else { return }
            let kids = children[project.id] ?? []
            rows.append(ProjectOutlineRow(project: project, depth: depth, hasChildren: !kids.isEmpty))
            kids.forEach { visit($0, depth: depth + 1) }
        }
        for project in self where project.parentID.map(ids.contains) != true { visit(project, depth: 0) }
        for project in self { visit(project, depth: 0) }
        return rows
    }

    /// The outline without whatever sits under a collapsed project.
    func outline(collapsed: Set<UUID>) -> [ProjectOutlineRow] {
        var rows: [ProjectOutlineRow] = []
        var hiddenBelow: Int?
        for row in outline() {
            if let depth = hiddenBelow, row.depth > depth { continue }
            hiddenBelow = nil
            rows.append(row)
            if row.hasChildren && collapsed.contains(row.id) { hiddenBelow = row.depth }
        }
        return rows
    }

    /// The project and everything under it.
    func subtreeIDs(of id: UUID) -> Set<UUID> {
        var result: Set<UUID> = [id]
        var frontier = [id]
        while let current = frontier.popLast() {
            for project in self where project.parentID == current && result.insert(project.id).inserted {
                frontier.append(project.id)
            }
        }
        return result
    }

    /// Where a project may be placed: anywhere except inside itself.
    func parentChoices(for id: UUID?) -> [ProjectOutlineRow] {
        guard let id else { return outline() }
        let excluded = subtreeIDs(of: id)
        return outline().filter { !excluded.contains($0.id) }
    }

    /// Puts a project directly above `targetID`, as its sibling. Returns `false` when that makes no sense.
    @discardableResult
    mutating func moveProject(_ id: UUID, before targetID: UUID) -> Bool {
        guard id != targetID,
              let from = firstIndex(where: { $0.id == id }),
              let target = first(where: { $0.id == targetID }),
              !subtreeIDs(of: id).contains(targetID) else { return false }
        var project = remove(at: from)
        project.parentID = target.parentID
        insert(project, at: firstIndex(where: { $0.id == targetID }) ?? endIndex)
        return true
    }
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
