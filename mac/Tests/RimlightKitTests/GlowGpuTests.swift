import CRimlight
import Metal
import XCTest

@testable import RimlightKit

/// Glow.metal on the GPU against glow_math.h on the CPU (docs/10-MACOS.md §1.4): renders the golden cases offscreen
/// (both edge styles, a notch shrunk with the surface, and the Flow movement's comets and ripples, whose motion the
/// shader applies after its first test) and compares each pixel's alpha. Skipped where the machine has no Metal device.
final class GlowGpuTests: XCTestCase {
    func testTheShaderDrawsTheProfile() throws {
        guard let device = MTLCreateSystemDefaultDevice() else { throw XCTSkip("No Metal device on this machine") }
        let glow = try MetalGlow(device: device)
        var texels = [Float](repeating: 0, count: 256)
        for i in 0..<64 { texels[i * 4] = 0.8; texels[i * 4 + 1] = 0.3; texels[i * 4 + 2] = 0.6; texels[i * 4 + 3] = 1 }
        texels.withUnsafeBufferPointer { glow.upload(gradient: $0.baseAddress!) }

        let width = 320, height = 180
        for c in try GlowMathTests.golden().cases {
            // The case's light state (its Flow movement included), edge style and notch on a small surface, so the
            // whole profile fits.
            var state = GlowMathTests.lightState(c.state)
            var surface = GlowMathTests.surface(c.surface, width: Int32(width), height: Int32(height))
            var mean = RlRgb(r: c.mean[0], g: c.mean[1], b: c.mean[2])
            var k = RlGlowConstants()
            XCTAssertEqual(rl_glow_constants(&state, &surface, c.surface.cornerRadiusDip,
                                             GlowMathTests.edgeStyle(c.edgeStyle), &mean, &k), RL_OK)

            let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .bgra8Unorm, width: width,
                                                                      height: height, mipmapped: false)
            descriptor.usage = [.renderTarget, .shaderRead]
            descriptor.storageMode = .managed
            let texture = try XCTUnwrap(device.makeTexture(descriptor: descriptor))
            let buffer = try XCTUnwrap(glow.makeCommandBuffer())
            glow.render(k, into: texture, buffer: buffer)
            let blit = try XCTUnwrap(buffer.makeBlitCommandEncoder())
            blit.synchronize(resource: texture)
            blit.endEncoding()
            buffer.commit()
            buffer.waitUntilCompleted()

            var pixels = [UInt8](repeating: 0, count: width * height * 4)
            texture.getBytes(&pixels, bytesPerRow: width * 4, from: MTLRegionMake2D(0, 0, width, height),
                             mipmapLevel: 0)
            var worst: Float = 0
            for y in stride(from: 0, to: height, by: 3) {
                for x in stride(from: 0, to: width, by: 3) {
                    var expected = gm_alpha(k, Float(x) + 0.5, Float(y) + 0.5)
                    if expected < 0.5 / 255 { expected = 0 }
                    let actual = Float(pixels[(y * width + x) * 4 + 3]) / 255
                    worst = max(worst, abs(actual - expected))
                }
            }
            // The dither adds up to half a code value, rounding another half, and the GPU's fast math a little more.
            XCTAssertLessThanOrEqual(worst, 1.5 / 255, "\(c.name): alpha off by \(worst * 255) code values")
        }
    }
}
