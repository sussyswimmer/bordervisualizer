import CRimlight
import Metal
import QuartzCore

/// The glow's GPU side (docs/10-MACOS.md §3.3): one device, queue and pipeline for every screen, and the 64-texel
/// palette gradient. Glow.metal draws one full-screen triangle per frame from the constants. Used by the render thread.
final class MetalGlow {
    /// Frames submitted to a screen but not yet on it, at most: a screen that stops taking frames (asleep, or slower)
    /// is skipped rather than allowed to block the render thread in nextDrawable.
    static let maxInFlight: Int32 = 2

    let device: MTLDevice
    private let queue: MTLCommandQueue
    private let pipeline: MTLRenderPipelineState
    private let gradient: MTLTexture

    init(device: MTLDevice) throws {
        self.device = device
        guard let queue = device.makeCommandQueue() else { throw MetalGlowError.noQueue }
        self.queue = queue

        let library = try device.makeLibrary(source: try ShaderSource.load(), options: nil)
        let descriptor = MTLRenderPipelineDescriptor()
        descriptor.vertexFunction = library.makeFunction(name: "glow_vertex")
        descriptor.fragmentFunction = library.makeFunction(name: "glow_fragment")
        // The shader writes premultiplied colour and alpha; the window server composites the layer.
        descriptor.colorAttachments[0].pixelFormat = .bgra8Unorm
        pipeline = try device.makeRenderPipelineState(descriptor: descriptor)

        let texture = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .rgba32Float, width: 64, height: 1,
                                                               mipmapped: false)
        texture.usage = .shaderRead
        texture.storageMode = device.hasUnifiedMemory ? .shared : .managed
        guard let gradient = device.makeTexture(descriptor: texture) else { throw MetalGlowError.noTexture }
        self.gradient = gradient
    }

    /// Uploads the 64 RGBA float texels (rl_loop_gradient).
    func upload(gradient texels: UnsafePointer<Float>) {
        gradient.replace(region: MTLRegionMake2D(0, 0, 64, 1), mipmapLevel: 0, withBytes: texels,
                         bytesPerRow: 64 * 4 * MemoryLayout<Float>.size)
    }

    /// Configures a screen's layer for the glow (main thread, when the overlay is made).
    func configure(_ layer: CAMetalLayer) {
        layer.device = device
        layer.pixelFormat = .bgra8Unorm
        layer.colorspace = CGColorSpace(name: CGColorSpace.sRGB)
        layer.framebufferOnly = true
        layer.isOpaque = false
        layer.maximumDrawableCount = 3
        layer.displaySyncEnabled = true
        layer.presentsWithTransaction = false
        layer.allowsNextDrawableTimeout = true
    }

    /// Draws one frame on a screen's layer and presents it. False when the screen has no drawable free (it hasn't
    /// shown the frames already sent), so nothing was presented.
    func draw(_ constants: RlGlowConstants, on layer: CAMetalLayer, inFlight: InFlightCounter) -> Bool {
        guard inFlight.value < Self.maxInFlight,
              let drawable = layer.nextDrawable(),
              let buffer = queue.makeCommandBuffer() else { return false }
        render(constants, into: drawable.texture, buffer: buffer)
        inFlight.add(1)
        drawable.addPresentedHandler { _ in inFlight.add(-1) }
        buffer.present(drawable)
        buffer.commit()
        return true
    }

    /// Encodes the glow into any BGRA8 texture: the overlay's drawable, or a test's offscreen texture.
    func render(_ constants: RlGlowConstants, into texture: MTLTexture, buffer: MTLCommandBuffer) {
        let pass = MTLRenderPassDescriptor()
        pass.colorAttachments[0].texture = texture
        pass.colorAttachments[0].loadAction = .dontCare // the triangle writes every pixel
        pass.colorAttachments[0].storeAction = .store
        guard let encoder = buffer.makeRenderCommandEncoder(descriptor: pass) else { return }
        var k = constants
        encoder.setRenderPipelineState(pipeline)
        encoder.setFragmentBytes(&k, length: MemoryLayout<RlGlowConstants>.size, index: 0)
        encoder.setFragmentTexture(gradient, index: 0)
        encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
        encoder.endEncoding()
    }

    func makeCommandBuffer() -> MTLCommandBuffer? { queue.makeCommandBuffer() }
}

enum MetalGlowError: Error {
    case noQueue
    case noTexture
}
