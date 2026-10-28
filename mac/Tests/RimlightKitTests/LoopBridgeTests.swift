import CRimlight
import Foundation
import XCTest

@testable import RimlightKit

/// The overlay loop's C ABI on the AOT library: the render thread's calls, in its order.
final class LoopBridgeTests: XCTestCase {
    private var directory: URL!

    override func setUpWithError() throws {
        directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: directory)
    }

    func testTheDemoDrawsEveryScreenAtTheFullRate() throws {
        let loop = try XCTUnwrap(CoreLoop(settingsDirectory: directory.path, demo: true))
        XCTAssertTrue(loop.demo)
        // An external display, and a 14-inch MacBook Pro whose notch spans 659...853 points, 32 points down.
        let surfaces = [RlSurface(widthPx: 1920, heightPx: 1080, pixelsPerPoint: 1, paused: 0, notchLeftPt: 0,
                                  notchRightPt: 0, notchHeightPt: 0, pad: 0),
                        RlSurface(widthPx: 3024, heightPx: 1964, pixelsPerPoint: 2, paused: 0, notchLeftPt: 659,
                                  notchRightPt: 853, notchHeightPt: 32, pad: 0)]
        XCTAssertEqual(rl_loop_set_surfaces(loop.handle, surfaces, 2, 10), RL_OK)
        XCTAssertEqual(rl_loop_set_display(loop.handle, 120, 0, 0), RL_OK)

        var now = 10.0
        var actions: [Int32] = []
        var pace = RlPace()
        var flowAmount: Float = 0
        var cometPhases = Set<Float>()
        for _ in 0..<30 {
            rl_loop_apply_inputs(loop.handle, now)
            var info = RlFrameInfo()
            XCTAssertEqual(rl_loop_begin_frame(loop.handle, 1.0 / 60, nil, 0, 0, 0, &info), RL_OK)
            XCTAssertEqual(info.demo, 1)
            XCTAssertNotNil(rl_loop_gradient(loop.handle))
            for index in 0..<2 {
                var k = RlGlowConstants()
                let action = rl_loop_plan_surface(loop.handle, Int32(index), &k)
                actions.append(action)
                if action > 0 {
                    XCTAssertGreaterThan(k.screenWidthPx, 0)
                    // The light goes round the notch, in the drawable's pixels.
                    XCTAssertEqual(k.notchHeightPx, index == 1 ? 64 : 0)
                    XCTAssertEqual(k.notchLeftPx, index == 1 ? 1318 : 0)
                    flowAmount = max(flowAmount, k.flowAmount)
                    cometPhases.insert(k.cometPhase)
                    XCTAssertEqual(rl_loop_surface_done(loop.handle, Int32(index), 1, Int64(now * 1000)), 0)
                }
            }
            now += 1.0 / 60
            XCTAssertEqual(rl_loop_end_frame(loop.handle, now, &pace), RL_OK)
        }
        XCTAssertEqual(Array(actions.prefix(2)), [RL_SURFACE_SHOW_AND_DRAW, RL_SURFACE_SHOW_AND_DRAW])
        XCTAssertTrue(actions.dropFirst(2).contains(RL_SURFACE_DRAW))
        XCTAssertEqual(pace.pace, RL_PACE_FULL)
        XCTAssertEqual(pace.fullRateHz, 60, accuracy: 1e-6) // the default 60 fps cap on a 120 Hz screen
        // The default movement is Flow: the demo's music moves comets round the edge, through the constants.
        XCTAssertGreaterThan(flowAmount, 0)
        XCTAssertGreaterThan(cometPhases.count, 1)
    }

    func testGlowOffIsSavedAndPublished() throws {
        let loop = try XCTUnwrap(CoreLoop(settingsDirectory: directory.path, demo: false))
        var settings = try XCTUnwrap(loop.settings)
        XCTAssertTrue(settings.enabled)
        settings.enabled = false
        XCTAssertEqual(loop.apply(settings), RL_OK)
        XCTAssertEqual(loop.settings?.enabled, false)
        let saved = try XCTUnwrap(Core.loadSettings(directory: directory.path).flatMap(AppSettings.decode))
        XCTAssertFalse(saved.enabled)
    }

    func testTheShaderSourceJoinsTheHeader() throws {
        let source = try ShaderSource.load()
        XCTAssertFalse(source.contains(ShaderSource.includeLine))
        XCTAssertTrue(source.contains("gm_alpha"))
        XCTAssertTrue(source.contains("glow_fragment"))
    }
}
