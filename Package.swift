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
        .executableTarget(name: "ValloSwitcher", dependencies: ["ValloCore"]),
        .executableTarget(name: "dump-layouts", dependencies: ["ValloCore"]),
        .testTarget(name: "ValloCoreTests", dependencies: ["ValloCore"]),
    ]
)
