import AudioToolbox
import CoreAudio
import CRimlight
import Foundation
import os

/// The Mac's system audio (docs/10-MACOS.md §2.1): a Core Audio process tap of everything the Mac plays, Rimlight
/// itself excluded, read through a private aggregate device that holds the tap and nothing else. It never opens an
/// input device: an aggregate exposes its sub-devices' inputs, so the output device joins it only when it has no
/// inputs (a headset may have a mic), and the tap refuses to start if the aggregate shows any input stream but the
/// tap's. The only
/// permission is "System Audio Recording Only", never the microphone. The IO proc is C (TapIO.c) and only mixes to mono into the
/// ring; the render thread drains the ring every frame.
///
/// Everything here runs on its own serial queue. The render thread reads `sampleRate` and `generation` (atomics) and
/// drains `ring`; it calls `setActive` when the glow starts or stops listening, which opens or closes the tap, so
/// nothing is captured while it isn't needed.
final class SystemAudioTap: @unchecked Sendable {
    /// 2^19 samples: 2.7 s at 192 kHz, 10.9 s at 48 kHz (doc 03 §1 asks for at least 2 s).
    static let ringCapacity: Int32 = 1 << 19
    /// Retries after a failure: 0.5 s, doubling to 5 s (doc 02).
    private static let firstRetry = 0.5, lastRetry = 5.0
    /// Sound from other apps, but only exact zeros from the tap for this long: probably no permission (§2.1).
    private static let cantHearSeconds = 10.0
    /// The tap is rebuilt at most this often while it hears only zeros (a macOS 26.5 beta bug, §2.1).
    private static let zeroRestartSeconds = 60.0
    /// A new tap's first checks wait this long: the permission prompt may be up, and the IO proc needs a moment.
    private static let graceSeconds = 3.0
    /// A burst of device changes (one switch can send several) rebuilds once, after this quiet time.
    private static let debounceSeconds = 0.2

    let ring: OpaquePointer
    private let context: OpaquePointer
    private let rate: OpaquePointer          // RlcCounter: the stream's sample rate, 0 while closed
    private let generationCounter: OpaquePointer // RlcCounter: bumped on every open, close and restart
    private let cantHearFlag: OpaquePointer  // RlcCounter: 1 while the tap seems to lack permission
    private let queue = DispatchQueue(label: "audio", qos: .userInteractive)
    private let log = Logger(subsystem: Bundle.main.bundleIdentifier ?? "app", category: "audio")

    // Queue only:
    private var wanted = false
    private var allowed = true // false until the first-run banner has explained the permission prompt
    private var tapID = AudioObjectID(kAudioObjectUnknown)
    private var aggregateID = AudioObjectID(kAudioObjectUnknown)
    private var procID: AudioDeviceIOProcID?
    private var retry = firstRetry
    private var retryPending = false
    private var lastZeroRestart = Date.distantPast
    private var openedAt = Date.distantPast
    private var health: DispatchSourceTimer?
    private var listening = false
    private var deviceChangeToken = 0

    init?() {
        guard let ring = rlc_ring_create(Self.ringCapacity), let context = rlc_tap_create(ring),
              let rate = rlc_counter_create(), let generation = rlc_counter_create(),
              let cantHear = rlc_counter_create() else { return nil }
        self.ring = ring
        self.context = context
        self.rate = rate
        generationCounter = generation
        cantHearFlag = cantHear
    }

    /// The open stream's sample rate, 0 while closed. Any thread.
    var sampleRate: Int32 { rlc_counter_load(rate) }

    /// Changes whenever the tap opens, closes or restarts. Any thread.
    var generation: Int32 { rlc_counter_load(generationCounter) }

    /// True while other apps play sound but the tap hears only silence: most likely "System Audio Recording" isn't
    /// allowed for this app. Any thread.
    var cantHear: Bool { rlc_counter_load(cantHearFlag) == 1 }

    /// Opens the tap (true) or closes it (false). Any thread.
    func setActive(_ on: Bool) {
        queue.async { [self] in
            guard on != wanted else { return }
            wanted = on
            retry = Self.firstRetry
            if on && allowed { open() } else if !on { close() }
        }
    }

    /// After a wake the devices may have changed under the tap: build it again if it is open. Any thread.
    func restartAfterWake() {
        queue.async { [self] in
            if procID != nil { restart("the Mac woke") }
        }
    }

    /// Holds the tap closed (false) until the first-run banner has said why macOS will ask for "System Audio
    /// Recording" (docs/10-MACOS.md §2.1), then lets it open (true). Any thread.
    func setAllowed(_ value: Bool) {
        queue.async { [self] in
            guard value != allowed else { return }
            allowed = value
            if value && wanted && procID == nil { open() } else if !value { close() }
        }
    }

    // MARK: - Queue

    private func open() {
        listenForDefaultDevice()
        do {
            try build()
            startHealthChecks() // from this open, so the grace period holds
        } catch {
            log.error("The system audio tap failed: \(String(describing: error), privacy: .public)")
            teardown()
            scheduleRetry()
        }
    }

    private func close() {
        stopHealthChecks()
        teardown()
        rlc_counter_store(cantHearFlag, 0)
    }

    private func restart(_ reason: String) {
        guard wanted && allowed else { return }
        log.info("Restarting the system audio tap: \(reason, privacy: .public)")
        teardown()
        open()
    }

    /// The tap failed while open: start over after the backoff, not at once, so a device that never delivers can't
    /// rebuild it every few seconds (and reset the analysis with it).
    private func fail(_ reason: String) {
        log.error("The system audio tap failed: \(reason, privacy: .public)")
        stopHealthChecks()
        teardown()
        scheduleRetry()
    }

    private func scheduleRetry() {
        guard wanted, !retryPending else { return }
        retryPending = true
        let delay = retry
        retry = min(retry * 2, Self.lastRetry)
        queue.asyncAfter(deadline: .now() + delay) { [self] in
            retryPending = false
            if wanted && allowed && procID == nil { open() }
        }
    }

    private func build() throws {
        let ownProcess = try Self.processObject(for: getpid())
        let description = CATapDescription(stereoGlobalTapButExcludeProcesses: [ownProcess])
        description.uuid = UUID()
        description.isPrivate = true
        description.muteBehavior = CATapMuteBehavior.unmuted
        description.name = "\(Core.appName) system audio"
        try check(AudioHardwareCreateProcessTap(description, &tapID), "AudioHardwareCreateProcessTap")

        let tapUID = try Self.string(tapID, kAudioTapPropertyUID)
        var format = AudioStreamBasicDescription()
        try Self.get(tapID, kAudioTapPropertyFormat, &format)

        // The output device clocks the aggregate, as in Apple's sample, but only when it has no input streams of its
        // own (speakers, not a headset with a mic): an aggregate exposes its sub-devices' inputs. Otherwise the
        // aggregate holds the tap alone. Either way the check below allows only the tap's stream.
        var aggregate: [String: Any] = [
            kAudioAggregateDeviceNameKey: "\(Core.appName) system audio",
            kAudioAggregateDeviceUIDKey: UUID().uuidString,
            kAudioAggregateDeviceIsPrivateKey: true,
            kAudioAggregateDeviceIsStackedKey: false,
            kAudioAggregateDeviceTapAutoStartKey: true,
            kAudioAggregateDeviceTapListKey: [[kAudioSubTapDriftCompensationKey: true, kAudioSubTapUIDKey: tapUID]],
        ]
        if let output = try? Self.defaultOutputDevice(), Self.inputStreamCount(output) == 0,
           let outputUID = try? Self.string(output, kAudioDevicePropertyDeviceUID) {
            aggregate[kAudioAggregateDeviceMainSubDeviceKey] = outputUID
            aggregate[kAudioAggregateDeviceSubDeviceListKey] = [[kAudioSubDeviceUIDKey: outputUID]]
        }
        try check(AudioHardwareCreateAggregateDevice(aggregate as CFDictionary, &aggregateID),
                  "AudioHardwareCreateAggregateDevice")
        let inputs = Self.inputStreamCount(aggregateID)
        if inputs != 1 {
            throw CoreAudioError(call: "The aggregate has \(inputs) input streams, not just the tap's", status: -1)
        }

        let isFloat = format.mFormatID == kAudioFormatLinearPCM && format.mFormatFlags & kAudioFormatFlagIsFloat != 0
            && format.mBitsPerChannel == 32
        rlc_tap_set_format(context, isFloat ? 1 : 0)
        if !isFloat { log.error("The tap's format isn't 32-bit float; it will be heard as silence") }

        var proc: AudioDeviceIOProcID?
        try check(AudioDeviceCreateIOProcID(aggregateID, rlc_tap_io_proc, UnsafeMutableRawPointer(context), &proc),
                  "AudioDeviceCreateIOProcID")
        procID = proc
        // The first start is where macOS asks for "System Audio Recording" (forums thread 771864).
        rlc_tap_reset_times(context)
        openedAt = Date()
        try check(AudioDeviceStart(aggregateID, procID), "AudioDeviceStart")

        // The IO proc runs at the aggregate's rate; the tap's format is the fallback.
        var nominal: Float64 = 0
        let sampleRate = (try? Self.get(aggregateID, kAudioDevicePropertyNominalSampleRate, &nominal)) != nil && nominal > 0
            ? nominal : format.mSampleRate
        rlc_counter_store(rate, Int32(sampleRate.rounded()))
        rlc_counter_add(generationCounter, 1)
        log.info("System audio tap open: \(sampleRate) Hz, \(format.mChannelsPerFrame) channels")
    }

    private func teardown() {
        if let procID, aggregateID != kAudioObjectUnknown {
            AudioDeviceStop(aggregateID, procID)
            AudioDeviceDestroyIOProcID(aggregateID, procID)
        }
        procID = nil
        if aggregateID != kAudioObjectUnknown { AudioHardwareDestroyAggregateDevice(aggregateID) }
        aggregateID = AudioObjectID(kAudioObjectUnknown)
        if tapID != kAudioObjectUnknown { AudioHardwareDestroyProcessTap(tapID) }
        tapID = AudioObjectID(kAudioObjectUnknown)
        if rlc_counter_load(rate) != 0 {
            rlc_counter_store(rate, 0)
            rlc_counter_add(generationCounter, 1)
        }
    }

    /// AirPods connecting, a display's speakers: the tap's main device must follow the default output.
    private func listenForDefaultDevice() {
        guard !listening else { return }
        listening = true
        var address = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDefaultOutputDevice,
                                                 mScope: kAudioObjectPropertyScopeGlobal,
                                                 mElement: kAudioObjectPropertyElementMain)
        let status = AudioObjectAddPropertyListenerBlock(AudioObjectID(kAudioObjectSystemObject), &address, queue) {
            [weak self] _, _ in
            self?.defaultDeviceChanged()
        }
        if status != noErr { log.error("Can't follow the default output device: \(status)") }
    }

    private func defaultDeviceChanged() {
        deviceChangeToken += 1
        let token = deviceChangeToken
        queue.asyncAfter(deadline: .now() + Self.debounceSeconds) { [self] in
            if token == deviceChangeToken { restart("the default output device changed") }
        }
    }

    /// Every 2 s while open: a stopped IO proc restarts the tap, and only zeros while other apps play mean it
    /// probably has no permission (or hit the zero-buffer bug, which a restart a minute fixes).
    private func startHealthChecks() {
        stopHealthChecks()
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now() + 2, repeating: 2, leeway: .milliseconds(500))
        timer.setEventHandler { [weak self] in self?.checkHealth() }
        timer.resume()
        health = timer
    }

    private func stopHealthChecks() {
        health?.cancel()
        health = nil
    }

    private func checkHealth() {
        guard wanted else { return }
        if procID == nil { return scheduleRetry() }
        guard Date().timeIntervalSince(openedAt) > Self.graceSeconds else { return }
        if rlc_tap_seconds_since_callback(context) > 2 { return fail("the tap stopped delivering audio") }
        retry = Self.firstRetry // it delivers: a later failure starts the backoff over
        // A format change (another rate on the output) would leave the analysis at the wrong rate.
        var nominal: Float64 = 0
        if (try? Self.get(aggregateID, kAudioDevicePropertyNominalSampleRate, &nominal)) != nil, nominal > 0,
           Int32(nominal.rounded()) != rlc_counter_load(rate) {
            return restart("the sample rate changed")
        }
        let silent = rlc_tap_seconds_since_sound(context) > Self.cantHearSeconds
        let othersPlay = silent && Self.anotherProcessPlays()
        rlc_counter_store(cantHearFlag, othersPlay ? 1 : 0)
        if othersPlay && Date().timeIntervalSince(lastZeroRestart) > Self.zeroRestartSeconds {
            lastZeroRestart = Date()
            restart("only silence while other apps play")
        }
    }

    // MARK: - Core Audio helpers

    struct CoreAudioError: Error, CustomStringConvertible {
        let call: String
        let status: OSStatus
        var description: String { "\(call) returned \(status)" }
    }

    private func check(_ status: OSStatus, _ call: String) throws {
        if status != noErr { throw CoreAudioError(call: call, status: status) }
    }

    private static func address(_ selector: AudioObjectPropertySelector) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal,
                                   mElement: kAudioObjectPropertyElementMain)
    }

    private static func get<T>(_ object: AudioObjectID, _ selector: AudioObjectPropertySelector,
                               _ value: inout T) throws {
        var address = Self.address(selector)
        var size = UInt32(MemoryLayout<T>.size)
        let status = withUnsafeMutablePointer(to: &value) {
            AudioObjectGetPropertyData(object, &address, 0, nil, &size, $0)
        }
        if status != noErr { throw CoreAudioError(call: "AudioObjectGetPropertyData(\(selector))", status: status) }
    }

    private static func string(_ object: AudioObjectID, _ selector: AudioObjectPropertySelector) throws -> String {
        var value: Unmanaged<CFString>?
        try get(object, selector, &value)
        guard let string = value?.takeRetainedValue() else {
            throw CoreAudioError(call: "AudioObjectGetPropertyData(\(selector)) gave no string", status: -1)
        }
        return string as String
    }

    private static func defaultOutputDevice() throws -> AudioObjectID {
        var device = AudioObjectID(kAudioObjectUnknown)
        try get(AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice, &device)
        if device == kAudioObjectUnknown { throw CoreAudioError(call: "No default output device", status: -1) }
        return device
    }

    /// The device's input streams: 0 for speakers, and for the aggregate only the tap's one stream is allowed.
    private static func inputStreamCount(_ device: AudioObjectID) -> Int {
        var address = AudioObjectPropertyAddress(mSelector: kAudioDevicePropertyStreams,
                                                 mScope: kAudioObjectPropertyScopeInput,
                                                 mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(device, &address, 0, nil, &size) == noErr else { return -1 }
        return Int(size) / MemoryLayout<AudioStreamID>.size
    }

    private static func processObject(for pid: pid_t) throws -> AudioObjectID {
        var address = Self.address(kAudioHardwarePropertyTranslatePIDToProcessObject)
        var pid = pid
        var object = AudioObjectID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        let status = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address,
                                                UInt32(MemoryLayout<pid_t>.size), &pid, &size, &object)
        if status != noErr || object == kAudioObjectUnknown {
            throw CoreAudioError(call: "kAudioHardwarePropertyTranslatePIDToProcessObject", status: status)
        }
        return object
    }

    /// Whether any process but this one is playing sound (kAudioProcessPropertyIsRunningOutput, macOS 14.2). The
    /// device-wide "running somewhere" isn't used: browsers and call apps keep the output open while silent.
    private static func anotherProcessPlays() -> Bool {
        var address = Self.address(kAudioHardwarePropertyProcessObjectList)
        let system = AudioObjectID(kAudioObjectSystemObject)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(system, &address, 0, nil, &size) == noErr, size > 0 else { return false }
        var objects = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(system, &address, 0, nil, &size, &objects) == noErr else { return false }
        let own = getpid()
        for object in objects {
            var pid: pid_t = 0
            var running: UInt32 = 0
            guard (try? get(object, kAudioProcessPropertyPID, &pid)) != nil, pid != own,
                  (try? get(object, kAudioProcessPropertyIsRunningOutput, &running)) != nil else { continue }
            if running != 0 { return true }
        }
        return false
    }
}
