// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ValloSwitcher",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "ValloSwitcher", targets: ["ValloSwitcher"]),
    ],
    dependencies: [
        // модель запятых: запуск ONNX и токенизатор
        .package(url: "https://github.com/microsoft/onnxruntime-swift-package-manager", exact: "1.24.2"),
        .package(url: "https://github.com/huggingface/swift-transformers", exact: "0.1.24"),
        // автообновление с GitHub Releases
        .package(url: "https://github.com/sparkle-project/Sparkle", exact: "2.10.0"),
    ],
    targets: [
        .target(name: "ValloCore"),
        .executableTarget(
            name: "ValloSwitcher",
            dependencies: [
                "ValloCore",
                .product(name: "onnxruntime", package: "onnxruntime-swift-package-manager"),
                .product(name: "Transformers", package: "swift-transformers"),
                .product(name: "Sparkle", package: "Sparkle"),
            ],
            // AppKit + C-колбэки тапа: строгая конкурентность Swift 6 тут только мешает
            swiftSettings: [.swiftLanguageMode(.v5)],
            // Sparkle.framework лежит в Contents/Frameworks (кладёт build-app.sh)
            linkerSettings: [.unsafeFlags(["-Xlinker", "-rpath", "-Xlinker", "@executable_path/../Frameworks"])]
        ),
        .executableTarget(name: "dump-layouts", dependencies: ["ValloCore"]),
        .testTarget(name: "ValloCoreTests", dependencies: ["ValloCore"]),
    ]
)
