import CRimlight
import Foundation
import XCTest

/// The Mac's glow math against the golden table src/Rimlight.Native.Tests writes from GlowProfile and GlowConstants
/// (docs/10-MACOS.md §1.4): the constants through the AOT library, the per-pixel alpha through glow_math.h, which
/// Glow.metal includes.
final class GlowMathTests: XCTestCase {
    struct Golden: Decodable {
        let cases: [Case]
    }

    struct Case: Decodable {
        let name: String
        let surface: Surface
        let state: State
        let mean: [Float]
        let constants: [Float]
        let samples: [Sample]
    }

    struct Surface: Decodable {
        let widthPx: Int32
        let heightPx: Int32
        let pixelsPerPoint: Float
        let cornerRadiusDip: Float
    }

    struct State: Decodable {
        let intensity, spread, coreThicknessDip, phase, pulse, visibility, ratio: Float
    }

    struct Sample: Decodable {
        let x, y, depth, alpha: Float
    }

    static func golden() throws -> Golden {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .appendingPathComponent("Fixtures/glow-golden.json")
        return try JSONDecoder().decode(Golden.self, from: Data(contentsOf: url))
    }

    static func lightState(_ s: State) -> RlLightState {
        RlLightState(colorA: RlRgb(r: 0.8, g: 0.2, b: 0.9), colorB: RlRgb(r: 0.1, g: 0.8, b: 0.9), ratio: s.ratio,
                     intensity: s.intensity, spread: s.spread, coreThicknessDip: s.coreThicknessDip, phase: s.phase,
                     pulse: s.pulse, visibility: s.visibility)
    }

    static func constants(_ values: [Float]) -> RlGlowConstants {
        RlGlowConstants(screenWidthPx: values[0], screenHeightPx: values[1], cornerRadiusPx: values[2],
                        shoulderPx: values[3], spreadPx: values[4], gain: values[5], pulse: values[6],
                        phase: values[7], visibility: values[8], colorRadiusPx: values[9], glowRadiusPx: values[10],
                        pad0: values[11], meanRed: values[12], meanGreen: values[13], meanBlue: values[14],
                        pad1: values[15])
    }

    static func values(_ k: RlGlowConstants) -> [Float] {
        [k.screenWidthPx, k.screenHeightPx, k.cornerRadiusPx, k.shoulderPx, k.spreadPx, k.gain, k.pulse, k.phase,
         k.visibility, k.colorRadiusPx, k.glowRadiusPx, k.pad0, k.meanRed, k.meanGreen, k.meanBlue, k.pad1]
    }

    func testTheLibraryComputesTheGoldenConstants() throws {
        for c in try Self.golden().cases {
            var state = Self.lightState(c.state)
            var mean = RlRgb(r: c.mean[0], g: c.mean[1], b: c.mean[2])
            var k = RlGlowConstants()
            XCTAssertEqual(rl_glow_constants(&state, c.surface.widthPx, c.surface.heightPx, c.surface.pixelsPerPoint,
                                             1, c.surface.cornerRadiusDip, &mean, &k), RL_OK)
            for (i, (actual, expected)) in zip(Self.values(k), c.constants).enumerated() {
                XCTAssertEqual(actual, expected, accuracy: 1e-5 * max(1, abs(expected)), "\(c.name) constant \(i)")
            }
        }
    }

    func testGlowMathMatchesGlowProfile() throws {
        for c in try Self.golden().cases {
            let k = Self.constants(c.constants)
            for s in c.samples {
                let depth = gm_depth(s.x, s.y, k.screenWidthPx, k.screenHeightPx, k.glowRadiusPx)
                XCTAssertEqual(depth, s.depth, accuracy: 1e-4 * max(1, s.depth), "\(c.name) depth at \(s.x),\(s.y)")
                XCTAssertEqual(gm_alpha(k, s.x, s.y), s.alpha, accuracy: 2e-6, "\(c.name) alpha at \(s.x),\(s.y)")
            }
        }
    }

    func testThePerimeterRunsClockwiseFromTheTopLeftCorner() {
        // Square corners on 1920 x 1080: 6000 px around, t = 0 at the top-left corner.
        XCTAssertEqual(gm_perimeter_t(960, 0.5, 1920, 1080, 0), 960.0 / 6000, accuracy: 1e-5)  // top middle
        XCTAssertEqual(gm_perimeter_t(1919.5, 540, 1920, 1080, 0), 2460.0 / 6000, accuracy: 1e-3) // right middle
        XCTAssertEqual(gm_perimeter_t(960, 1079.5, 1920, 1080, 0), 3960.0 / 6000, accuracy: 1e-3) // bottom middle
        XCTAssertEqual(gm_perimeter_t(0.5, 540, 1920, 1080, 0), 5460.0 / 6000, accuracy: 1e-3)    // left middle
    }

    func testThePerimeterIsContinuousThroughRoundedCorners() {
        // Walk just inside the edge of a 1280 x 720 surface with a 200 px colour radius: no jump bigger than a step.
        let w: Float = 1280, h: Float = 720, r: Float = 200, inset: Float = 3
        var points: [(Float, Float)] = []
        for x in stride(from: inset, through: w - inset, by: 2) { points.append((x, inset)) }
        for y in stride(from: inset, through: h - inset, by: 2) { points.append((w - inset, y)) }
        for x in stride(from: w - inset, through: inset, by: -2) { points.append((x, h - inset)) }
        for y in stride(from: h - inset, through: inset, by: -2) { points.append((inset, y)) }
        var previous = gm_perimeter_t(points[0].0, points[0].1, w, h, r)
        for (x, y) in points.dropFirst() {
            let t = gm_perimeter_t(x, y, w, h, r)
            var step = abs(t - previous)
            step = min(step, 1 - step) // 0.999 and 0.001 are neighbours
            XCTAssertLessThan(step, 0.01, "jump at \(x),\(y)")
            previous = t
        }
    }
}
