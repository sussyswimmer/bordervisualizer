import CRimlight
import Foundation
import XCTest

/// The Mac's glow math against the golden table src/Rimlight.Native.Tests writes from GlowProfile and GlowConstants
/// (docs/10-MACOS.md §1.4): the constants through the AOT library, from the screen as the app describes it (RlSurface,
/// its notch in points included) and the edge style, by the loop's own path (rl_glow_constants calls what
/// rl_loop_plan_surface does); the per-pixel alpha through glow_math.h, which Glow.metal includes, evaluated on the
/// golden constants, so the shader's math is checked on its own whatever the constants. The Flow movement's light state
/// crosses the ABI in RlLightState and its motion is checked per sample too (the perimeter coordinate and the motion
/// the alpha is multiplied by), with unit tests of its comets and ripples.
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
        let flow: Flow? // the Flow cases' only
    }

    /// The light state's Flow movement (Core's FlowMotion).
    struct Flow: Decodable {
        let amount, cometPhase: Float
        let ripples: [Ripple] // four
    }

    struct Ripple: Decodable {
        let origin, radius, strength: Float
    }

    struct Sample: Decodable {
        let x, y, depth, alpha: Float
        let lineDepth: Float? // the Line style's samples only
        let t, motion: Float? // the Flow cases' samples only: GlowProfile.PerimeterT and GlowProfile.Motion
    }

    static func golden() throws -> Golden {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .appendingPathComponent("Fixtures/glow-golden.json")
        return try JSONDecoder().decode(Golden.self, from: Data(contentsOf: url))
    }

    static func lightState(_ s: State) -> RlLightState {
        RlLightState(colorA: RlRgb(r: 0.8, g: 0.2, b: 0.9), colorB: RlRgb(r: 0.1, g: 0.8, b: 0.9), ratio: s.ratio,
                     intensity: s.intensity, spread: s.spread, coreThicknessDip: s.coreThicknessDip, phase: s.phase,
                     pulse: s.pulse, visibility: s.visibility, flow: flowMotion(s.flow))
    }

    /// The Flow movement as RlLightState carries it; none (all 0) without one.
    static func flowMotion(_ f: Flow?) -> RlFlowMotion {
        guard let f else { return RlFlowMotion() }
        func ripple(_ i: Int) -> RlRipple {
            RlRipple(origin: f.ripples[i].origin, radius: f.ripples[i].radius, strength: f.ripples[i].strength)
        }
        return RlFlowMotion(amount: f.amount, cometPhase: f.cometPhase, ripple0: ripple(0), ripple1: ripple(1),
                            ripple2: ripple(2), ripple3: ripple(3))
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

    /// The constants from the table's 40 floats: the 20 of the profile and the notch, then the Flow movement's register
    /// and its four ripples (C's ripples[4], a tuple in Swift), four floats each.
    static func constants(_ values: [Float]) -> RlGlowConstants {
        func ripple(_ i: Int) -> RlGlowRipple {
            RlGlowRipple(origin: values[i], radius: values[i + 1], strength: values[i + 2], pad: values[i + 3])
        }
        return RlGlowConstants(screenWidthPx: values[0], screenHeightPx: values[1], cornerRadiusPx: values[2],
                               shoulderPx: values[3], spreadPx: values[4], gain: values[5], pulse: values[6],
                               phase: values[7], visibility: values[8], colorRadiusPx: values[9],
                               glowRadiusPx: values[10], linePx: values[11], meanRed: values[12],
                               meanGreen: values[13], meanBlue: values[14], topCornerRadiusPx: values[15],
                               notchLeftPx: values[16], notchRightPx: values[17], notchHeightPx: values[18],
                               notchRadiusPx: values[19], flowAmount: values[20], cometPhase: values[21],
                               flowPad0: values[22], flowPad1: values[23],
                               ripples: (ripple(24), ripple(28), ripple(32), ripple(36)))
    }

    static func values(_ k: RlGlowConstants) -> [Float] {
        var floats: [Float] = [
            k.screenWidthPx, k.screenHeightPx, k.cornerRadiusPx, k.shoulderPx, k.spreadPx, k.gain, k.pulse, k.phase,
            k.visibility, k.colorRadiusPx, k.glowRadiusPx, k.linePx, k.meanRed, k.meanGreen, k.meanBlue,
            k.topCornerRadiusPx, k.notchLeftPx, k.notchRightPx, k.notchHeightPx, k.notchRadiusPx,
        ]
        floats += [k.flowAmount, k.cometPhase, k.flowPad0, k.flowPad1]
        for r in [k.ripples.0, k.ripples.1, k.ripples.2, k.ripples.3] {
            floats += [r.origin, r.radius, r.strength, r.pad]
        }
        return floats
    }

    func testTheLibraryComputesTheGoldenConstants() throws {
        for c in try Self.golden().cases {
            var state = Self.lightState(c.state)
            var surface = Self.surface(c.surface)
            var mean = RlRgb(r: c.mean[0], g: c.mean[1], b: c.mean[2])
            var k = RlGlowConstants()
            XCTAssertEqual(rl_glow_constants(&state, &surface, c.surface.cornerRadiusDip, Self.edgeStyle(c.edgeStyle),
                                             &mean, &k), RL_OK)
            XCTAssertEqual(c.constants.count, 40, c.name)
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
                // The Flow movement: the perimeter coordinate it is taken at (0.99999 and 0 are neighbours), and the
                // motion the alpha is multiplied by.
                if let expectedT = s.t, let expectedMotion = s.motion {
                    let t = gm_perimeter_t(s.x, s.y, k.screenWidthPx, k.screenHeightPx, k.colorRadiusPx)
                    let apart = abs(t - expectedT)
                    XCTAssertLessThan(min(apart, 1 - apart), 1e-5, "\(c.name) t at \(s.x),\(s.y)")
                    XCTAssertEqual(gm_motion(k, s.x, s.y), expectedMotion, accuracy: 2e-6,
                                   "\(c.name) motion at \(s.x),\(s.y)")
                }
                XCTAssertEqual(gm_alpha(k, s.x, s.y), s.alpha, accuracy: 2e-6, "\(c.name) alpha at \(s.x),\(s.y)")
            }
        }
    }

    func testTheGoldenTableHasBothStylesANotchAndTheFlowMovement() throws {
        // The Line style, the notch and the Flow movement are only as safe as the cases that cover them.
        let cases = try Self.golden().cases
        XCTAssertTrue(cases.contains { $0.edgeStyle == "Line" && $0.constants[11] > 0 })
        XCTAssertTrue(cases.contains { $0.edgeStyle == "Line" && $0.constants[18] > 0 })
        XCTAssertTrue(cases.contains { $0.edgeStyle == "Glow" && $0.constants[18] > 0 })
        // The Flow movement in both styles, round the notch, at the full amount and part of it.
        let flow = cases.filter { $0.constants[20] > 0 }
        XCTAssertTrue(flow.contains { $0.edgeStyle == "Line" && $0.constants[18] > 0 && $0.constants[20] == 1 })
        XCTAssertTrue(flow.contains { $0.edgeStyle == "Glow" && $0.constants[20] < 1 })
        // A ripple just landed (radius 0), and one whose fronts run across the wrap at the top-left corner.
        var ripples: [(origin: Float, radius: Float, strength: Float)] = []
        for c in flow {
            for i in stride(from: 24, to: 40, by: 4) {
                ripples.append((c.constants[i], c.constants[i + 1], c.constants[i + 2]))
            }
        }
        XCTAssertTrue(ripples.contains { $0.strength > 0 && $0.radius == 0 })
        XCTAssertTrue(ripples.contains { $0.strength > 0 && ($0.origin + $0.radius > 1 || $0.origin - $0.radius < 0) })
        // Every Flow sample carries its perimeter coordinate and motion.
        XCTAssertTrue(flow.allSatisfy { c in c.samples.allSatisfy { $0.t != nil && $0.motion != nil } })
    }

    // MARK: - The Flow movement's pieces

    func testTheCometsAreBrightestAtTheirHeadsAndContinuous() {
        // One head, at 1 - phase; nothing jumps anywhere round the loop.
        let phase: Float = 0.3
        XCTAssertEqual(gm_comets(0.7, phase), 1, accuracy: 1e-6)
        XCTAssertLessThan(gm_comets(0.2, phase), 0.2) // half a perimeter behind it the tail is down to exp(-0.5 / 0.3)
        XCTAssertGreaterThan(gm_comets(0.71, phase), gm_comets(0.69, phase)) // the tail fades slower than the head
        var previous = gm_comets(0, phase)
        for i in 1...20_000 {
            let value = gm_comets(Float(i) / 20_000, phase)
            XCTAssertLessThan(abs(value - previous), 0.02, "jump at \(Float(i) / 20_000)")
            XCTAssertTrue(value > 0 && value <= 1)
            previous = value
        }
    }

    func testARippleRunsBothWaysAndWraps() {
        // Two fronts 0.05 either side of 0.3, as bright as the ripple there; nothing without a strength.
        XCTAssertEqual(gm_ripple_at(0.25, 0.3, 0.05, 0.8), 0.8, accuracy: 1e-6)
        XCTAssertEqual(gm_ripple_at(0.35, 0.3, 0.05, 0.8), 0.8, accuracy: 1e-6)
        XCTAssertEqual(gm_ripple_at(0.2, 0.3, 0.05, 0.8), gm_ripple_at(0.4, 0.3, 0.05, 0.8), accuracy: 1e-6)
        XCTAssertLessThan(gm_ripple_at(0.3, 0.3, 0.05, 0.8), 0.8) // behind the fronts it has faded
        XCTAssertEqual(gm_ripple_at(0.3, 0.3, 0.05, 0), 0)
        // From 0.99 the fronts reach 0.97 and, across the wrap, 0.01.
        XCTAssertEqual(gm_ripple_at(0.01, 0.99, 0.02, 1), 1, accuracy: 1e-5)
        XCTAssertEqual(gm_ripple_at(0.97, 0.99, 0.02, 1), 1, accuracy: 1e-5)
        // Continuous through its fronts and its origin.
        var previous = gm_ripple_at(0, 0.5, 0.1, 1)
        for i in 1...20_000 {
            let value = gm_ripple_at(Float(i) / 20_000, 0.5, 0.1, 1)
            XCTAssertLessThan(abs(value - previous), 0.01, "jump at \(Float(i) / 20_000)")
            previous = value
        }
    }

    func testWithoutAFlowAmountTheAlphaIsTheProfilesExactly() throws {
        // The Pulse movement and Idle Glow: no motion, and the light exactly as before the Flow movement.
        for c in try Self.golden().cases where c.constants[20] == 0 {
            let k = Self.constants(c.constants)
            for s in c.samples {
                XCTAssertEqual(gm_motion(k, s.x, s.y), 1)
                XCTAssertEqual(gm_alpha(k, s.x, s.y), gm_profile_alpha(k, s.x, s.y))
            }
        }
    }

    func testTheMotionDimsTheEdgeBetweenTheCometsAndBlendsToItsMeanDeepInside() throws {
        let golden = try XCTUnwrap(Self.golden().cases.first { $0.name == "flow-glow-1080p-half" })
        var k = Self.constants(golden.constants)
        k.flowAmount = 1
        k.cometPhase = 0
        k.ripples = (RlGlowRipple(), RlGlowRipple(), RlGlowRipple(), RlGlowRipple())
        // At the edge: 1 at a comet's head (t 0, the top-left corner), and near the floor of 0.5 at the end of its
        // tail, just before the next head (t 0.5).
        XCTAssertEqual(gm_motion_at(k, 0, 0), 1, accuracy: 1e-6)
        XCTAssertEqual(gm_motion_at(k, 0.47, 0), 0.5, accuracy: 0.02)
        // Deep inside it is the mean, wherever the comets are.
        XCTAssertEqual(gm_motion_at(k, 0, 1), 0.62886227, accuracy: 1e-6)
        XCTAssertEqual(gm_motion_at(k, 0.4, 1), 0.62886227, accuracy: 1e-6)
        // Half the amount is half way to 1.
        k.flowAmount = 0.5
        XCTAssertEqual(gm_motion_at(k, 0.4, 1), 1 - 0.5 * (1 - 0.62886227), accuracy: 1e-6)
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
