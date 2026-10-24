// Writes the Mac app's own tasks.json for the sample workspace, so the C# tests can prove
// they read and write exactly what the Swift code produces.
//
//   ./build.sh --check
//   swiftc -target "$(uname -m)-apple-macos13.0" -I build/obj -L build/obj -lTodoCore \
//       -o build/obj/make-mac-fixture windows/tools/make-mac-fixture.swift
//   build/obj/make-mac-fixture <output directory>
import Foundation
import TodoCore

guard CommandLine.arguments.count > 1 else {
    FileHandle.standardError.write(Data("usage: make-mac-fixture <output directory>\n".utf8))
    exit(64)
}

var calendar = Calendar(identifier: .gregorian)
calendar.timeZone = TimeZone(identifier: "Europe/Berlin")!
calendar.firstWeekday = 2

var components = DateComponents()
components.year = 2026
components.month = 10
components.day = 23
components.hour = 15
components.minute = 30
let now = calendar.date(from: components)!

let directory = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
let store = DataFileStore(directory: directory)
try store.save(AppData.sample(now: now, calendar: calendar))
print("Wrote \(store.fileURL.path)")
