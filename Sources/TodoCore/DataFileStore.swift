import Foundation

/// The app keeps everything in one folder, so copying that folder backs up everything:
///
///     TodoApp/
///       tasks.json                     the live data
///       Backups/2026-11-02/tasks.json  the data as it stood when that day began
///       Attachments/2026-11-02/...     files attached on that day
public enum DatedFolder {
    /// "2026-11-02", in the user's own time zone.
    public static func name(for date: Date, calendar: Calendar = .current) -> String {
        let parts = calendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d-%02d-%02d", parts.year ?? 0, parts.month ?? 0, parts.day ?? 0)
    }

    public static func isDatedName(_ name: String) -> Bool {
        let parts = name.split(separator: "-", omittingEmptySubsequences: false)
        return parts.map(\.count) == [4, 2, 2] && parts.allSatisfy { $0.allSatisfy(\.isASCII) && $0.allSatisfy(\.isNumber) }
    }
}

public enum AttachmentFiles {
    /// Deletes stored copies no task refers to. `referenced` holds paths relative to `directory`.
    /// Looks one level into dated folders and removes those left empty.
    public static func removeUnreferenced(in directory: URL, keeping referenced: Set<String>) {
        let fileManager = FileManager.default
        guard let names = try? fileManager.contentsOfDirectory(atPath: directory.path) else { return }
        for name in names where !name.hasPrefix(".") {
            let url = directory.appendingPathComponent(name)
            var isDirectory: ObjCBool = false
            let exists = fileManager.fileExists(atPath: url.path, isDirectory: &isDirectory)
            guard exists, isDirectory.boolValue, DatedFolder.isDatedName(name) else {
                if !referenced.contains(name) { try? fileManager.removeItem(at: url) }
                continue
            }
            let inner = (try? fileManager.contentsOfDirectory(atPath: url.path)) ?? []
            for file in inner where !file.hasPrefix(".") && !referenced.contains("\(name)/\(file)") {
                try? fileManager.removeItem(at: url.appendingPathComponent(file))
            }
            if (try? fileManager.contentsOfDirectory(atPath: url.path))?.isEmpty == true {
                try? fileManager.removeItem(at: url)
            }
        }
    }
}

/// Reads and writes `AppData` as a single JSON file.
public struct DataFileStore: Sendable {
    public let directory: URL

    public init(directory: URL) {
        self.directory = directory
    }

    public var fileURL: URL { directory.appendingPathComponent("tasks.json") }

    public var backupsDirectory: URL { directory.appendingPathComponent("Backups", isDirectory: true) }

    /// Keeps a copy of the data file in today's backup folder, once a day. Call it only after the file
    /// has loaded, so a backup is never a broken file. Returns the copy, or `nil` when there was nothing to do.
    @discardableResult
    public func backUpDataFile(now: Date = Date(), calendar: Calendar = .current) throws -> URL? {
        let fileManager = FileManager.default
        guard fileManager.fileExists(atPath: fileURL.path) else { return nil }
        let folder = backupsDirectory.appendingPathComponent(DatedFolder.name(for: now, calendar: calendar), isDirectory: true)
        let copy = folder.appendingPathComponent(fileURL.lastPathComponent)
        guard !fileManager.fileExists(atPath: copy.path) else { return nil }
        try fileManager.createDirectory(at: folder, withIntermediateDirectories: true)
        try fileManager.copyItem(at: fileURL, to: copy)
        return copy
    }

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
