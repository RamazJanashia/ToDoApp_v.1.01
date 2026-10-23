import AppKit
import SwiftUI
import TodoCore

extension NSColor {
    convenience init(hex: UInt32, alpha: CGFloat = 1) {
        self.init(
            srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
            green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255,
            alpha: alpha
        )
    }
}

extension Color {
    init(hex: UInt32, opacity: Double = 1) {
        self.init(
            .sRGB,
            red: Double((hex >> 16) & 0xFF) / 255,
            green: Double((hex >> 8) & 0xFF) / 255,
            blue: Double(hex & 0xFF) / 255,
            opacity: opacity
        )
    }

    /// A colour that resolves against the view's appearance, so one token serves light and dark mode.
    static func adaptive(light: NSColor, dark: NSColor) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? dark : light
        })
    }

    /// Ink at an opacity: white on dark, black on light.
    static func ink(dark: CGFloat, light: CGFloat) -> Color {
        adaptive(light: NSColor(white: 0, alpha: light), dark: NSColor(white: 1, alpha: dark))
    }
}

/// The app's whole visual vocabulary. Monochrome first; colour only carries meaning.
/// Dark values follow the reference design; light values mirror them on white.
enum Theme {
    // Surfaces
    static let background = Color.adaptive(light: NSColor(hex: 0xFBFBFC), dark: NSColor(hex: 0x08090B))
    static let panel = Color.adaptive(light: NSColor(hex: 0xF2F2F4), dark: NSColor(hex: 0x101114))
    static let raised = Color.adaptive(light: NSColor(hex: 0xFFFFFF), dark: NSColor(hex: 0x16171B))
    static let raisedHover = Color.adaptive(light: NSColor(hex: 0xFAFAFB), dark: NSColor(hex: 0x1A1B1F))
    static let raisedSelected = Color.adaptive(light: NSColor(hex: 0xF4F5F8), dark: NSColor(hex: 0x1E1F24))

    // Lines and states
    static let separator = Color.ink(dark: 0.075, light: 0.08)
    static let strongSeparator = Color.ink(dark: 0.13, light: 0.14)
    static let hover = Color.ink(dark: 0.045, light: 0.04)
    static let selection = Color.ink(dark: 0.11, light: 0.07)
    static let focusRing = Color.ink(dark: 0.2, light: 0.22)
    static let selectedOutline = Color.ink(dark: 0.22, light: 0.2)
    static let track = Color.ink(dark: 0.1, light: 0.08)

    // Text
    static let primaryText = Color.ink(dark: 0.93, light: 0.87)
    static let secondaryText = Color.ink(dark: 0.55, light: 0.52)
    static let tertiaryText = Color.ink(dark: 0.33, light: 0.32)

    // Controls
    static let checkboxFill = Color.ink(dark: 0.6, light: 0.22)
    static let checkboxHover = Color.ink(dark: 0.8, light: 0.36)
    static let chip = Color.ink(dark: 0.04, light: 0.04)
    static let chipHover = Color.ink(dark: 0.08, light: 0.07)
    static let chipSelected = Color.ink(dark: 0.16, light: 0.12)

    // Chart (one quiet series; emphasis by brightness only)
    static let chartBar = Color.ink(dark: 0.22, light: 0.16)
    static let chartToday = Color.ink(dark: 0.55, light: 0.45)
    static let chartHover = Color.ink(dark: 0.85, light: 0.8)

    // Meaning — slightly deeper on white to keep contrast.
    static let green = Color.adaptive(light: NSColor(hex: 0x1FA463), dark: NSColor(hex: 0x34C77B))
    static let red = Color.adaptive(light: NSColor(hex: 0xE0443C), dark: NSColor(hex: 0xEF5B53))
    static let blue = Color.adaptive(light: NSColor(hex: 0x2F6FE4), dark: NSColor(hex: 0x4C8DF6))

    enum Font {
        static let pageTitle = SwiftUI.Font.system(size: 28, weight: .bold)
        static let body = SwiftUI.Font.system(size: 14)
        static let rowTitle = SwiftUI.Font.system(size: 14.5)
        static let caption = SwiftUI.Font.system(size: 12)
        static let small = SwiftUI.Font.system(size: 11)
        static let sectionHeader = SwiftUI.Font.system(size: 11.5, weight: .medium)
    }

    enum Layout {
        /// Lists and the dashboard stop growing here and centre themselves, so rows stay
        /// readable on wide windows and in full screen.
        static let readingWidth: CGFloat = 860
        static let dashboardWidth: CGFloat = 960
        static let pageMargin: CGFloat = 32
    }

    enum Motion {
        static let quick = Animation.easeOut(duration: 0.16)
        static let standard = Animation.spring(response: 0.32, dampingFraction: 0.86)
    }
}

extension ProjectColor {
    var color: Color {
        Color.adaptive(light: NSColor(hex: lightHex), dark: NSColor(hex: hex))
    }

    /// Deeper variants that hold up on a white background.
    private var lightHex: UInt32 {
        switch self {
        case .blue: return 0x2F6FE4
        case .green: return 0x22A06B
        case .red: return 0xE0443C
        case .orange: return 0xE5822B
        case .yellow: return 0xD9A400
        case .purple: return 0x8B5CF6
        case .pink: return 0xE0529C
        case .teal: return 0x1FA99A
        case .graphite: return 0x8E8E93
        case .white: return 0x3A3A3C
        }
    }
}

extension Priority {
    /// Only High uses colour; the rest step down in brightness.
    var tint: Color {
        switch self {
        case .high: return Theme.red
        case .medium: return Color.ink(dark: 0.62, light: 0.55)
        case .low: return Color.ink(dark: 0.36, light: 0.32)
        case .none: return Theme.tertiaryText
        }
    }
}

extension TaskStatus {
    var icon: String {
        switch self {
        case .todo: return "circle"
        case .inProgress: return "circle.lefthalf.filled"
        case .done: return "checkmark.circle"
        }
    }
}

/// System / Light / Dark, applied app-wide.
enum AppearancePreference: String, CaseIterable, Identifiable {
    case system
    case light
    case dark

    var id: String { rawValue }

    var title: String {
        switch self {
        case .system: return "System"
        case .light: return "Light"
        case .dark: return "Dark"
        }
    }

    var icon: String {
        switch self {
        case .system: return "circle.lefthalf.filled"
        case .light: return "sun.max"
        case .dark: return "moon"
        }
    }

    var nsAppearance: NSAppearance? {
        switch self {
        case .system: return nil
        case .light: return NSAppearance(named: .aqua)
        case .dark: return NSAppearance(named: .darkAqua)
        }
    }
}
