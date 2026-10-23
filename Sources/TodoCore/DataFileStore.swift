import Foundation

/// Reads and writes `AppData` as a single JSON file.
public struct DataFileStore: Sendable {
    public let directory: URL

    public init(directory: URL) {
        self.directory = directory
    }

    public var fileURL: URL { directory.appendingPathComponent("tasks.json") }

    /// `~/Library/Application Support/TodoApp`
    public static var defaultDirectory: URL {
        let fileManager = FileManager.default
        let base = fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? fileManager.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support")
        return base.appendingPathComponent("TodoApp", isDirectory: true)
    }

    /// `nil` when nothing has been saved yet.
    public func load() throws -> AppData? {
        guard FileManager.default.fileExists(atPath: fileURL.path) else { return nil }
        let data = try Data(contentsOf: fileURL)
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return try decoder.decode(AppData.self, from: data)
    }

    /// Writes atomically, so a crash mid-save never leaves a half-written file.
    public func save(_ appData: AppData) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(appData).write(to: fileURL, options: .atomic)
    }

    /// Moves an unreadable data file aside so it is never overwritten, and returns its new location.
    @discardableResult
    public func quarantineDataFile(now: Date = Date()) throws -> URL {
        // The random suffix keeps two failures in the same second from colliding.
        let suffix = UUID().uuidString.prefix(8)
        let destination = directory.appendingPathComponent("tasks.unreadable-\(Int(now.timeIntervalSince1970))-\(suffix).json")
        try FileManager.default.moveItem(at: fileURL, to: destination)
        return destination
    }
}
