import SwiftUI
import TodoCore

/// "WORKSPACE", "NOTES" — small uppercase labels with open tracking.
struct SectionHeader: View {
    let title: String

    init(_ title: String) {
        self.title = title
    }

    var body: some View {
        Text(title.uppercased())
            .font(Theme.Font.sectionHeader)
            .tracking(0.9)
            .foregroundColor(Theme.secondaryText.opacity(0.85))
    }
}

struct Hairline: View {
    var color: Color = Theme.separator

    var body: some View {
        Rectangle().fill(color).frame(height: 1)
    }
}

struct VerticalHairline: View {
    var body: some View {
        Rectangle().fill(Theme.separator).frame(width: 1)
    }
}

/// A borderless symbol button that brightens on hover.
struct IconButton: View {
    let systemName: String
    var size: CGFloat = 14
    var weight: Font.Weight = .regular
    var isActive = false
    let help: String
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: size, weight: weight))
                .foregroundColor(hovering || isActive ? Theme.primaryText : Theme.secondaryText)
                .frame(width: 28, height: 28)
                .background(
                    RoundedRectangle(cornerRadius: 7, style: .continuous)
                        .fill(isActive ? Theme.selection : (hovering ? Theme.hover : .clear))
                )
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .help(help)
        // The help text without its shortcut, e.g. "New Task" from "New Task (⌘N)".
        .accessibilityLabel(help.components(separatedBy: " (").first ?? help)
    }
}

/// Round task checkbox: a soft grey disc when open, a green ring and tick when done.
struct CheckCircle: View {
    let isDone: Bool
    var size: CGFloat = 20
    let action: () -> Void

    @State private var hovering = false
    @State private var pulse = false

    var body: some View {
        Button {
            withAnimation(Theme.Motion.press) { pulse = true }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.18) {
                withAnimation(Theme.Motion.quick) { pulse = false }
            }
            action()
        } label: {
            ZStack {
                if isDone {
                    Circle().stroke(Theme.green, lineWidth: 1.6)
                    Image(systemName: "checkmark")
                        .font(.system(size: size * 0.46, weight: .bold))
                        .foregroundColor(Theme.green)
                } else {
                    Circle().fill(hovering ? Theme.checkboxHover : Theme.checkboxFill)
                    if hovering {
                        Image(systemName: "checkmark")
                            .font(.system(size: size * 0.44, weight: .bold))
                            .foregroundColor(Theme.background.opacity(0.7))
                    }
                }
            }
            .frame(width: size, height: size)
            .scaleEffect(pulse ? 0.86 : 1)
            .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .help(isDone ? "Mark as Incomplete" : "Complete")
        .accessibilityLabel(isDone ? "Mark as Incomplete" : "Complete")
        .animation(Theme.Motion.quick, value: isDone)
    }
}

/// The ⓘ that appears on a hovered task and opens its details.
struct DetailsButton: View {
    @EnvironmentObject private var state: AppState

    let taskID: UUID
    let isVisible: Bool

    var body: some View {
        IconButton(systemName: "info.circle", size: 14, help: "Show Details (double-click)") {
            withAnimation(Theme.Motion.standard) { state.revealInInspector(taskID, focusTitle: false) }
        }
        .opacity(isVisible ? 1 : 0)
        .allowsHitTesting(isVisible)
        .accessibilityLabel("Show details")
    }
}

/// A square checkbox for subtasks.
struct CheckSquare: View {
    let isDone: Bool
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Image(systemName: isDone ? "checkmark.square.fill" : "square")
                .font(.system(size: 15, weight: .regular))
                .foregroundColor(isDone ? Theme.green : (hovering ? Theme.primaryText : Theme.secondaryText))
                .frame(width: 20, height: 20)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .accessibilityLabel(isDone ? "Mark Subtask as Incomplete" : "Complete Subtask")
    }
}

struct PriorityFlag: View {
    let priority: Priority
    var dimmed = false

    var body: some View {
        if priority != .none {
            Image(systemName: "flag")
                .font(.system(size: 13, weight: .regular))
                .foregroundColor(dimmed ? Theme.tertiaryText : priority.tint)
                .help("\(priority.title) priority")
        }
    }
}

/// The thin green completion bar under a page title.
struct ProgressLine: View {
    let fraction: Double

    var body: some View {
        GeometryReader { proxy in
            ZStack(alignment: .leading) {
                Capsule().fill(Theme.track)
                Capsule()
                    .fill(Theme.green.opacity(0.85))
                    .frame(width: max(fraction > 0 ? 4 : 0, proxy.size.width * min(max(fraction, 0), 1)))
            }
        }
        .frame(height: 4)
        .animation(Theme.Motion.standard, value: fraction)
        .accessibilityElement()
        .accessibilityLabel("\(Int((fraction * 100).rounded())) percent complete")
    }
}

struct EmptyStateView: View {
    let icon: String
    let title: String
    var message: String?

    var body: some View {
        VStack(spacing: 10) {
            Image(systemName: icon)
                .font(.system(size: 26, weight: .light))
                .foregroundColor(Theme.tertiaryText)
            Text(title)
                .font(.system(size: 14, weight: .medium))
                .foregroundColor(Theme.secondaryText)
            if let message {
                Text(message)
                    .font(Theme.Font.caption)
                    .foregroundColor(Theme.tertiaryText)
                    .multilineTextAlignment(.center)
            }
        }
        .frame(maxWidth: 260)
        .padding(.vertical, 40)
    }
}

/// One choice in a popover list: icon, title, checkmark when selected.
struct OptionRow: View {
    var icon: String?
    var iconColor: Color = Theme.secondaryText
    let title: String
    var isSelected = false
    var shortcut: String?
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                if let icon {
                    Image(systemName: icon)
                        .font(.system(size: 13))
                        .foregroundColor(iconColor)
                        .frame(width: 18)
                }
                Text(title)
                    .font(.system(size: 13))
                    .foregroundColor(Theme.primaryText)
                Spacer(minLength: 12)
                if let shortcut {
                    Text(shortcut).font(Theme.Font.small).foregroundColor(Theme.tertiaryText)
                }
                if isSelected {
                    Image(systemName: "checkmark")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(Theme.primaryText)
                }
            }
            .padding(.horizontal, 10)
            .frame(height: 30)
            .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(hovering ? Theme.selection : .clear))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }
}

/// The dark surface every popover sits on.
struct PopoverSurface<Content: View>: View {
    var width: CGFloat = 230
    @ViewBuilder let content: () -> Content

    var body: some View {
        VStack(alignment: .leading, spacing: 1) {
            content()
        }
        .padding(6)
        .frame(width: width)
        .background(Theme.panel)
    }
}

extension View {
    /// Tells VoiceOver that a row built on a tap gesture acts like a button, keeping the
    /// controls inside it reachable.
    func accessibleRow(_ label: String, isSelected: Bool = false, action: @escaping () -> Void) -> some View {
        accessibilityElement(children: .contain)
            .accessibilityLabel(label)
            .accessibilityAddTraits(isSelected ? [.isButton, .isSelected] : .isButton)
            .accessibilityAction(.default, action)
    }

    /// Subtle rounded highlight for hover and selection.
    func rowHighlight(selected: Bool, hovering: Bool, cornerRadius: CGFloat = 8) -> some View {
        background(
            RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                .fill(selected ? Theme.selection : (hovering ? Theme.hover : .clear))
        )
    }
}
