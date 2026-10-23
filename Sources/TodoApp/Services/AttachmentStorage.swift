import AppKit
import Foundation
import ImageIO
import TodoCore
import UniformTypeIdentifiers

/// Small, downsampled previews of image attachments, decoded once per file.
private let thumbnailCache = NSCache<NSString, NSImage>()

/// Keeps copies of attached files inside the app's data folder, so attachments
/// survive the original being moved or deleted.
struct AttachmentStorage {
    let directory: URL

    func importFile(at source: URL) throws -> Attachment {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let id = UUID()
        let storedName = "\(id.uuidString)-\(source.lastPathComponent)"
        try FileManager.default.copyItem(at: source, to: directory.appendingPathComponent(storedName))
        return Attachment(id: id, kind: .file, name: source.lastPathComponent, location: storedName)
    }

    func url(for attachment: Attachment) -> URL? {
        switch attachment.kind {
        case .file: return directory.appendingPathComponent(attachment.location)
        case .link: return URL(string: attachment.location)
        }
    }

    func isImage(_ attachment: Attachment) -> Bool {
        guard attachment.kind == .file,
              let type = UTType(filenameExtension: (attachment.name as NSString).pathExtension) else { return false }
        return type.conforms(to: .image)
    }

    func thumbnail(for attachment: Attachment) -> NSImage? {
        guard isImage(attachment), let url = url(for: attachment) else { return nil }
        let key = attachment.location as NSString
        if let cached = thumbnailCache.object(forKey: key) { return cached }
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: 120
        ]
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              let cgImage = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else { return nil }
        let image = NSImage(cgImage: cgImage, size: NSSize(width: cgImage.width, height: cgImage.height))
        thumbnailCache.setObject(image, forKey: key)
        return image
    }

    /// File size and kind, e.g. "PDF document · 1.2 MB".
    func detail(for attachment: Attachment) -> String {
        guard attachment.kind == .file, let url = url(for: attachment) else {
            return URL(string: attachment.location)?.host ?? attachment.location
        }
        let values = try? url.resourceValues(forKeys: [.fileSizeKey, .localizedTypeDescriptionKey])
        let kind = values?.localizedTypeDescription ?? "File"
        guard let size = values?.fileSize else { return "\(kind) · missing" }
        return "\(kind) · \(ByteCountFormatter.string(fromByteCount: Int64(size), countStyle: .file))"
    }

    /// Deletes stored copies no task refers to any more. Runs at launch, which keeps
    /// undo of a deleted task or attachment safe for the whole session.
    func removeFiles(notIn referenced: Set<String>) {
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: directory.path) else { return }
        for name in names where !referenced.contains(name) && !name.hasPrefix(".") {
            try? FileManager.default.removeItem(at: directory.appendingPathComponent(name))
        }
    }

    /// Accepts "example.com/page" as well as full URLs.
    static func link(from text: String) -> Attachment? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, !trimmed.contains(" ") else { return nil }
        let candidate = trimmed.contains("://") ? trimmed : "https://\(trimmed)"
        guard let url = URL(string: candidate), let host = url.host, host.contains(".") || host == "localhost" else { return nil }
        let name = host.hasPrefix("www.") ? String(host.dropFirst(4)) : host
        return Attachment(kind: .link, name: name, location: url.absoluteString)
    }
}
