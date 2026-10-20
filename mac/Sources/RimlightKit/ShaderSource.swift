import Foundation

/// The glow shader's source: Glow.metal with glow_math.h in place of its include line, since runtime compilation
/// can't resolve includes. The app bundle carries the joined file (build.sh); tests and `swift run` join the sources.
enum ShaderSource {
    static let includeLine = "#include \"glow_math.h\""

    static func load() throws -> String {
        if let url = Bundle.main.url(forResource: "Glow", withExtension: "metal") {
            return try String(contentsOf: url, encoding: .utf8)
        }
        // mac/Sources/RimlightKit/ShaderSource.swift → mac/
        let mac = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent()
        let header = try String(contentsOf: mac.appendingPathComponent("Sources/CRimlight/include/glow_math.h"),
                                encoding: .utf8)
        let shader = try String(contentsOf: mac.appendingPathComponent("Resources/Shaders/Glow.metal"),
                                encoding: .utf8)
        return join(shader: shader, header: header)
    }

    static func join(shader: String, header: String) -> String {
        shader.replacingOccurrences(of: includeLine, with: header)
    }
}
