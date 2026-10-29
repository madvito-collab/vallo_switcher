// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ValloSwitcher",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "ValloSwitcher", targets: ["ValloSwitcher"]),
    ],
    targets: [
        .target(name: "ValloCore"),
        .executableTarget(
            name: "ValloSwitcher",
            dependencies: ["ValloCore"],
            // AppKit + C-колбэки тапа: строгая конкурентность Swift 6 тут только мешает
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
        .executableTarget(name: "dump-layouts", dependencies: ["ValloCore"]),
        .testTarget(name: "ValloCoreTests", dependencies: ["ValloCore"]),
    ]
)
