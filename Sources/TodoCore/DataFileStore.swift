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

/// Thrown when the data file was written by a newer version of the app.
public enum DataFileError: Error {
    case newerFormat(Int)
}

public enum AttachmentFiles {
    /// Where a stored file lives, or `nil` when `location` points outside `directory`.
    /// `location` comes from the data file, which can be edited by hand or synced from elsewhere.
    public static func url(forLocation location: String, in directory: URL) -> URL? {
        let base = directory.standardizedFileURL.resolvingSymlinksInPath()
        let candidate = directory.appendingPathComponent(location).standardizedFileURL.resolvingSymlinksInPath()
        guard candidate.path.hasPrefix(base.path + "/") else { return nil }
        return candidate
    }

    /// Deletes stored copies no task refers to. `referenced` holds paths relative to `directory`.
    /// Looks one level into dated folders and removes those left empty. Dated folders named
    /// `keepingFoldersFrom` or later are left alone, so recent attachments get a grace period.
    public static func removeUnreferenced(in directory: URL, keeping referenced: Set<String>, keepingFoldersFrom firstKeptFolder: String? = nil) {
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
            // Dated names sort as dates.
            if let firstKeptFolder, name >= firstKeptFolder { continue }
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

/// Which links may be attached and opened. Opening a URL hands it to whatever app claims its
/// scheme, so anything other than the web and mail is refused, both on the way in and on the
/// way out (the data file can be edited by hand).
public enum AttachmentLink {
    private static let openableSchemes: Set<String> = ["http", "https", "mailto"]

    public static func canOpen(_ location: String) -> Bool {
        guard let scheme = URL(string: location)?.scheme?.lowercased() else { return false }
        return openableSchemes.contains(scheme)
    }

    /// Accepts "example.com/page" and "mailto:a@b.c" as well as full web addresses.
    public static func attachment(from text: String) -> Attachment? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed.rangeOfCharacter(from: .whitespacesAndNewlines) == nil else { return nil }

        if trimmed.lowercased().hasPrefix("mailto:") {
            let address = trimmed.dropFirst("mailto:".count).prefix { $0 != "?" }
            guard address.contains("@"), URL(string: trimmed) != nil else { return nil }
            return Attachment(kind: .link, name: String(address), location: trimmed)
        }

        let candidate = trimmed.contains("://") ? trimmed : "https://\(trimmed)"
        guard let url = URL(string: candidate),
              let scheme = url.scheme?.lowercased(), scheme == "http" || scheme == "https",
              url.user == nil,
              let host = url.host, host.contains(".") || host == "localhost" else { return nil }
        let name = host.hasPrefix("www.") ? String(host.dropFirst(4)) : host
        return Attachment(kind: .link, name: name, location: url.absoluteString)
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
        // Atomic, so a crash can't leave a half-written backup that blocks the day's real one.
        try Data(contentsOf: fileURL).write(to: copy, options: .atomic)
        return copy
    }

    /// Removes all but the newest `count` dated backups. Anything else in the folder is left alone.
    public func pruneBackups(keeping count: Int) {
        let fileManager = FileManager.default
        guard let names = try? fileManager.contentsOfDirectory(atPath: backupsDirectory.path) else { return }
        for name in names.filter(DatedFolder.isDatedName).sorted().dropLast(count) {
            try? fileManager.removeItem(at: backupsDirectory.appendingPathComponent(name))
        }
    }

    /// Every stored attachment the data file or a backup refers to, so restoring a backup
    /// always finds its files. `nil` means that can't be known (no readable data file, a
    /// quarantined file, a damaged backup), and nothing may be cleaned up.
    public func referencedAttachmentPaths() -> Set<String>? {
        let fileManager = FileManager.default
        guard fileManager.fileExists(atPath: fileURL.path),
              let names = try? fileManager.contentsOfDirectory(atPath: directory.path),
              !names.contains(where: { $0.hasPrefix("tasks.unreadable-") }) else { return nil }

        let backups = (try? fileManager.contentsOfDirectory(atPath: backupsDirectory.path)) ?? []
        let backupFiles = backups.filter(DatedFolder.isDatedName)
            .map { backupsDirectory.appendingPathComponent($0).appendingPathComponent(fileURL.lastPathComponent) }
            .filter { fileManager.fileExists(atPath: $0.path) }

        var referenced: Set<String> = []
        for file in [fileURL] + backupFiles {
            guard let appData = try? decode(file) else { return nil }
            for task in appData.tasks {
                for attachment in task.attachments where attachment.kind == .file {
                    referenced.insert(attachment.location)
                }
            }
        }
        return referenced
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
        return try decode(fileURL)
    }

    private struct VersionOnly: Decodable {
        var version: Int?
    }

    private func decode(_ file: URL) throws -> AppData {
        let data = try Data(contentsOf: file)
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        // Checked first: a newer file may hold values this version can't decode at all.
        if let version = (try? decoder.decode(VersionOnly.self, from: data))?.version, version > AppData.currentVersion {
            throw DataFileError.newerFormat(version)
        }
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
