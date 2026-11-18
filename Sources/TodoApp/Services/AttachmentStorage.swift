import AppKit
import Foundation
import ImageIO
import TodoCore
import UniformTypeIdentifiers

/// Small, downsampled previews of image attachments, decoded once per file.
private let thumbnailCache = NSCache<NSString, NSImage>()

/// Keeps copies of attached files inside the app's data folder, one folder per day,
/// so attachments survive the original being moved or deleted.
struct AttachmentStorage {
    let directory: URL

    func importFile(at source: URL) throws -> Attachment {
        let folder = DatedFolder.name(for: Date())
        try FileManager.default.createDirectory(at: directory.appendingPathComponent(folder), withIntermediateDirectories: true)
        let id = UUID()
        let storedName = "\(folder)/\(id.uuidString)-\(source.lastPathComponent)"
        try FileManager.default.copyItem(at: source, to: directory.appendingPathComponent(storedName))
        return Attachment(id: id, kind: .file, name: source.lastPathComponent, location: storedName)
    }

    func url(for attachment: Attachment) -> URL? {
        switch attachment.kind {
        case .file: return AttachmentFiles.url(forLocation: attachment.location, in: directory)
        case .link: return AttachmentLink.canOpen(attachment.location) ? URL(string: attachment.location) : nil
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
        guard attachment.kind == .file else {
            return URL(string: attachment.location)?.host ?? attachment.location
        }
        guard let url = url(for: attachment) else { return "File · missing" }
        let values = try? url.resourceValues(forKeys: [.fileSizeKey, .localizedTypeDescriptionKey])
        let kind = values?.localizedTypeDescription ?? "File"
        guard let size = values?.fileSize else { return "\(kind) · missing" }
        return "\(kind) · \(ByteCountFormatter.string(fromByteCount: Int64(size), countStyle: .file))"
    }

    /// Deletes stored copies nothing refers to any more, apart from those in dated folders
    /// named `keepingFoldersFrom` or later.
    func removeFiles(notIn referenced: Set<String>, keepingFoldersFrom firstKeptFolder: String) {
        AttachmentFiles.removeUnreferenced(in: directory, keeping: referenced, keepingFoldersFrom: firstKeptFolder)
    }
}
