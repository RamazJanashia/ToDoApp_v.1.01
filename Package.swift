// swift-tools-version: 5.8
import PackageDescription

let package = Package(
    name: "TodoApp",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "TodoApp", targets: ["TodoApp"])
    ],
    targets: [
        // Pure Foundation: models, recurrence, querying, persistence. No UI.
        .target(
            name: "TodoCore",
            path: "Sources/TodoCore"
        ),
        // The SwiftUI + AppKit application.
        .executableTarget(
            name: "TodoApp",
            dependencies: ["TodoCore"],
            path: "Sources/TodoApp"
        ),
        // Behaviour checks for TodoCore. Run with `swift run TodoCoreChecks`.
        // (A plain executable because the Command Line Tools ship without XCTest.)
        .executableTarget(
            name: "TodoCoreChecks",
            dependencies: ["TodoCore"],
            path: "Tests/TodoCoreChecks"
        )
    ]
)
