import CRimlight
import Foundation
import XCTest

/// The Mac's glow math against the golden table src/Rimlight.Native.Tests writes from GlowProfile and GlowConstants
/// (docs/10-MACOS.md §1.4): the constants through the AOT library, from the screen as the app describes it (RlSurface,
/// its notch in points included) and the edge style, by the loop's own path (rl_glow_constants calls what
/// rl_loop_plan_surface does); the per-pixel alpha through glow_math.h, which Glow.metal includes, evaluated on the
/// golden constants, so the shader's math is checked on its own whatever the constants.
final class GlowMathTests: XCTestCase {
    struct Golden: Decodable {
        let cases: [Case]
    }

    struct Case: Decodable {
        let name: String
        let edgeStyle: String // Glow | Line
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
        let notchLeftPt, notchRightPt, notchHeightPt: Float // all 0 without a notch
    }

    struct State: Decodable {
        let intensity, spread, coreThicknessDip, phase, pulse, visibility, ratio: Float
    }

    struct Sample: Decodable {
        let x, y, depth, alpha: Float
        let lineDepth: Float? // the Line style's samples only
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

    /// The case's screen as the overlay controller describes it to the loop, its notch included. Drawn `width` pixels
    /// wide instead, the notch shrinks with it.
    static func surface(_ s: Surface, width: Int32? = nil, height: Int32? = nil) -> RlSurface {
        let widthPx = width ?? s.widthPx
        let shrink = Float(widthPx) / Float(s.widthPx)
        return RlSurface(widthPx: widthPx, heightPx: height ?? s.heightPx, pixelsPerPoint: s.pixelsPerPoint, paused: 0,
                         notchLeftPt: s.notchLeftPt * shrink, notchRightPt: s.notchRightPt * shrink,
                         notchHeightPt: s.notchHeightPt * shrink, pad: 0)
    }

    static func edgeStyle(_ name: String) -> Int32 {
        name == "Line" ? RL_EDGE_LINE : RL_EDGE_GLOW
    }

    static func constants(_ values: [Float]) -> RlGlowConstants {
        RlGlowConstants(screenWidthPx: values[0], screenHeightPx: values[1], cornerRadiusPx: values[2],
                        shoulderPx: values[3], spreadPx: values[4], gain: values[5], pulse: values[6],
                        phase: values[7], visibility: values[8], colorRadiusPx: values[9], glowRadiusPx: values[10],
                        linePx: values[11], meanRed: values[12], meanGreen: values[13], meanBlue: values[14],
                        topCornerRadiusPx: values[15], notchLeftPx: values[16], notchRightPx: values[17],
                        notchHeightPx: values[18], notchRadiusPx: values[19])
    }

    static func values(_ k: RlGlowConstants) -> [Float] {
        [k.screenWidthPx, k.screenHeightPx, k.cornerRadiusPx, k.shoulderPx, k.spreadPx, k.gain, k.pulse, k.phase,
         k.visibility, k.colorRadiusPx, k.glowRadiusPx, k.linePx, k.meanRed, k.meanGreen, k.meanBlue,
         k.topCornerRadiusPx, k.notchLeftPx, k.notchRightPx, k.notchHeightPx, k.notchRadiusPx]
    }

    func testTheLibraryComputesTheGoldenConstants() throws {
        for c in try Self.golden().cases {
            var state = Self.lightState(c.state)
            var surface = Self.surface(c.surface)
            var mean = RlRgb(r: c.mean[0], g: c.mean[1], b: c.mean[2])
            var k = RlGlowConstants()
            XCTAssertEqual(rl_glow_constants(&state, &surface, c.surface.cornerRadiusDip, Self.edgeStyle(c.edgeStyle),
                                             &mean, &k), RL_OK)
            XCTAssertEqual(c.constants.count, 20, c.name)
            for (i, (actual, expected)) in zip(Self.values(k), c.constants).enumerated() {
                XCTAssertEqual(actual, expected, accuracy: 1e-5 * max(1, abs(expected)), "\(c.name) constant \(i)")
            }
        }
    }

    func testGlowMathMatchesGlowProfile() throws {
        for c in try Self.golden().cases {
            let k = Self.constants(c.constants)
            for s in c.samples {
                // The depths gm_alpha takes: the light's field and the screen's own shape, both round the notch.
                var depth = gm_depth(s.x, s.y, k.screenWidthPx, k.screenHeightPx, k.glowRadiusPx)
                var lineDepth = gm_depth_corners(s.x, s.y, k.screenWidthPx, k.screenHeightPx, k.topCornerRadiusPx,
                                                 k.cornerRadiusPx)
                if k.notchHeightPx > 0 {
                    let notch = gm_notch_distance(s.x, s.y, k.notchLeftPx, k.notchRightPx, k.notchHeightPx,
                                                  k.notchRadiusPx)
                    depth = gm_around_notch(depth, notch, k.notchRadiusPx)
                    lineDepth = gm_around_notch(lineDepth, notch, k.notchRadiusPx)
                }
                XCTAssertEqual(depth, s.depth, accuracy: 1e-4 * max(1, s.depth), "\(c.name) depth at \(s.x),\(s.y)")
                if let expected = s.lineDepth {
                    XCTAssertEqual(lineDepth, expected, accuracy: 1e-4 * max(1, expected),
                                   "\(c.name) line depth at \(s.x),\(s.y)")
                }
                XCTAssertEqual(gm_alpha(k, s.x, s.y), s.alpha, accuracy: 2e-6, "\(c.name) alpha at \(s.x),\(s.y)")
            }
        }
    }

    func testTheGoldenTableHasBothStylesAndANotch() throws {
        // The Line style and the notch are only as safe as the cases that cover them.
        let cases = try Self.golden().cases
        XCTAssertTrue(cases.contains { $0.edgeStyle == "Line" && $0.constants[11] > 0 })
        XCTAssertTrue(cases.contains { $0.edgeStyle == "Line" && $0.constants[18] > 0 })
        XCTAssertTrue(cases.contains { $0.edgeStyle == "Glow" && $0.constants[18] > 0 })
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
