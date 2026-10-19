// swift-tools-version:5.9
// The Mac app (docs/10-MACOS.md). mac/build.sh publishes Core as libRimlightCore.dylib into .build-core first, then
// builds this package against it and assembles the app bundle; `swift test` needs that dylib too.
import PackageDescription

let coreLibDirectory = Context.packageDirectory + "/.build-core"

let package = Package(
    name: "RimlightMac",
    platforms: [.macOS("14.4")],
    products: [
        .executable(name: "RimlightMac", targets: ["RimlightMac"]),
    ],
    targets: [
        // The C ABI of libRimlightCore (src/Rimlight.Native/Exports.cs). The second rpath finds the dylib from
        // `swift build` and `swift test` output; build.sh removes it from the bundled app, which uses the first.
        .target(
            name: "CRimlight",
            linkerSettings: [
                .linkedLibrary("RimlightCore"),
                .linkedFramework("CoreAudio"),
                .unsafeFlags([
                    "-L", coreLibDirectory,
                    "-Xlinker", "-rpath", "-Xlinker", "@executable_path/../Frameworks",
                    "-Xlinker", "-rpath", "-Xlinker", coreLibDirectory,
                ]),
            ]),
        .target(name: "RimlightKit", dependencies: ["CRimlight"]),
        .executableTarget(name: "RimlightMac", dependencies: ["RimlightKit"]),
        // The golden table is read by path (#filePath), not bundled.
        .testTarget(name: "RimlightKitTests", dependencies: ["RimlightKit", "CRimlight"], exclude: ["Fixtures"]),
    ]
)
