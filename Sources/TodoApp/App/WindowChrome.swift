import AppKit
import SwiftUI

/// Reaches the hosting NSWindow to give it the near-black, seamless title bar.
struct WindowConfigurator: NSViewRepresentable {
    var onWindow: (NSWindow) -> Void

    func makeNSView(context: Context) -> NSView {
        ConfiguringView(onWindow: onWindow)
    }

    /// SwiftUI rebuilds the toolbar when its content changes, which drops the flexible space,
    /// so put it back. On the next turn, once that rebuild has finished.
    func updateNSView(_ nsView: NSView, context: Context) {
        guard let window = nsView.window else { return }
        DispatchQueue.main.async { ConfiguringView.pushTrailingItemsRight(in: window) }
    }

    private final class ConfiguringView: NSView {
        let onWindow: (NSWindow) -> Void

        init(onWindow: @escaping (NSWindow) -> Void) {
            self.onWindow = onWindow
            super.init(frame: .zero)
        }

        required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

        /// On macOS 13 SwiftUI lines toolbar items up right after a centred title. A flexible
        /// space after the title sends search and the inspector toggle to the right edge.
        /// The toolbar may not exist yet when the view joins the window, so retry briefly.
        static func pushTrailingItemsRight(in window: NSWindow, attempts: Int = 20) {
            guard let toolbar = window.toolbar,
                  let centered = toolbar.centeredItemIdentifier,
                  let index = toolbar.items.firstIndex(where: { $0.itemIdentifier == centered }) else {
                if attempts > 0 {
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
                        pushTrailingItemsRight(in: window, attempts: attempts - 1)
                    }
                }
                return
            }
            guard !toolbar.items.contains(where: { $0.itemIdentifier == .flexibleSpace }) else { return }
            toolbar.insertItem(withItemIdentifier: .flexibleSpace, at: index + 1)
        }

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            guard let window else { return }
            // Don't set window.backgroundColor: AppKit then rebuilds the frame with the
            // SwiftUI content stacked above the title bar, hiding and disabling the
            // close/minimise/zoom buttons. The toolbar background colours the title bar.
            window.titlebarAppearsTransparent = true
            window.titlebarSeparatorStyle = .none
            window.tabbingMode = .disallowed
            onWindow(window)
            Self.pushTrailingItemsRight(in: window)
            // SwiftUI focuses the first text field when the window opens; start with
            // nothing focused so the arrow keys move through the list right away.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) {
                if window.firstResponder is NSTextView { window.makeFirstResponder(nil) }
            }
        }
    }
}

/// Installs an app-wide key-down monitor for list navigation while the view is on screen.
struct KeyboardMonitor: NSViewRepresentable {
    let handler: (NSEvent) -> Bool

    func makeNSView(context: Context) -> NSView {
        context.coordinator.install(handler)
        return NSView()
    }

    func updateNSView(_ nsView: NSView, context: Context) {}

    static func dismantleNSView(_ nsView: NSView, coordinator: Coordinator) {
        coordinator.uninstall()
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    final class Coordinator {
        private var monitor: Any?

        func install(_ handler: @escaping (NSEvent) -> Bool) {
            monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
                handler(event) ? nil : event
            }
        }

        func uninstall() {
            if let monitor { NSEvent.removeMonitor(monitor) }
            monitor = nil
        }
    }
}

/// An invisible, wide grab area on a column edge that resizes the neighbouring column.
struct ColumnResizer: View {
    @Binding var width: Double
    let range: ClosedRange<Double>
    /// +1 when dragging right widens the column (sidebar), -1 when it narrows it (inspector).
    let direction: Double
    var showsLine = true

    @State private var dragStartWidth: Double?
    @State private var hovering = false
    @State private var cursorPushed = false

    var body: some View {
        Rectangle()
            .fill(showsLine ? Theme.separator : Color.clear)
            .frame(width: 1)
            .overlay(
                Color.clear
                    .frame(width: 9)
                    .contentShape(Rectangle())
                    .onHover { inside in
                        hovering = inside
                        updateCursor()
                    }
                    .gesture(
                        DragGesture(minimumDistance: 1, coordinateSpace: .global)
                            .onChanged { value in
                                let start = dragStartWidth ?? width
                                dragStartWidth = start
                                let proposed = start + value.translation.width * direction
                                width = min(max(proposed, range.lowerBound), range.upperBound)
                                updateCursor()
                            }
                            .onEnded { _ in
                                dragStartWidth = nil
                                updateCursor()
                            }
                    )
            )
            .zIndex(1)
            // If the column goes away under the pointer (e.g. ⌥⌘I), give the arrow back.
            .onDisappear {
                if cursorPushed { NSCursor.pop() }
                cursorPushed = false
            }
    }

    /// The resize cursor shows while hovering or dragging; pushes and pops stay balanced.
    private func updateCursor() {
        let wantsCursor = hovering || dragStartWidth != nil
        if wantsCursor && !cursorPushed {
            NSCursor.resizeLeftRight.push()
            cursorPushed = true
        } else if !wantsCursor && cursorPushed {
            NSCursor.pop()
            cursorPushed = false
        }
    }
}
