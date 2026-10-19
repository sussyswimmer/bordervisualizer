import AppKit
import CRimlight
import ImageIO
import os

/// What's playing, and the album colours (docs/10-MACOS.md §4.2). Music and Spotify announce track changes with
/// distributed notifications, which need no permission. Music's cover is read with an Apple event, only on a track
/// change and only while Music is running (an Apple event to a player that isn't running would launch it); that asks
/// once for Automation permission ("control Music"). Spotify's cover would need a network fetch from Spotify's server,
/// which Maxwell hasn't allowed (Q4), so Spotify and every other player get the manual colours.
///
/// Doc 05 §1's rules: a change is read 250 ms after the last event; a missing cover keeps the previous colours and is
/// read once more after 750 ms, then the manual colours show. Everything runs on one serial queue; `onChange` is
/// called on the main thread.
final class NowPlayingService: @unchecked Sendable {
    struct Track: Equatable {
        let title: String?
        let artist: String?
        let app: String   // the player's name
        let isPlaying: Bool
        var id: String { "\(app)|\(artist ?? "")|\(title ?? "")" } // doc 05 §1's track ID
    }

    static let musicID = "com.apple.Music"
    static let spotifyID = "com.spotify.client"
    private static let debounce = 0.25, coverRetry = 0.75
    private static let coverSize = 64 // doc 05 §1: decode to at most 64 x 64

    private let loop: CoreLoop
    private let queue = DispatchQueue(label: "media", qos: .utility)
    private let log = Logger(subsystem: Bundle.main.bundleIdentifier ?? "app", category: "media")
    private let onChange: @MainActor (Track?, Bool) -> Void
    private var observers: [NSObjectProtocol] = []

    // Queue only:
    private var tracks: [String: Track] = [:] // the latest from each player, by bundle ID
    private var shown: Track?
    private var coverFor: String?           // the track whose cover was last read
    private var pending = 0
    private var automationDenied = false

    /// `onChange(track, automationDenied)`: the track to show (nil: none) and whether Music refused the cover.
    init(loop: CoreLoop, onChange: @escaping @MainActor (Track?, Bool) -> Void) {
        self.loop = loop
        self.onChange = onChange
    }

    func start() {
        let distributed = DistributedNotificationCenter.default()
        observers.append(distributed.addObserver(forName: Notification.Name("com.apple.Music.playerInfo"), object: nil,
                                                 queue: nil) { [weak self] note in
            self?.received(Self.musicID, app: "Music", info: note.userInfo)
        })
        observers.append(distributed.addObserver(forName: Notification.Name("com.spotify.client.PlaybackStateChanged"),
                                                 object: nil, queue: nil) { [weak self] note in
            self?.received(Self.spotifyID, app: "Spotify", info: note.userInfo)
        })
        observers.append(NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didTerminateApplicationNotification, object: nil, queue: nil
        ) { [weak self] note in
            let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
            guard let id = app?.bundleIdentifier else { return }
            self?.queue.async { self?.playerQuit(id) }
        })
        // What Music plays already (Spotify announces itself on its next change).
        queue.async { [self] in
            if Self.isRunning(Self.musicID), let track = Self.musicNowPlaying() {
                tracks[Self.musicID] = track
                schedule()
            }
        }
    }

    // MARK: - Queue

    private func received(_ bundleID: String, app: String, info: [AnyHashable: Any]?) {
        let state = info?["Player State"] as? String
        let track = Track(title: info?["Name"] as? String, artist: info?["Artist"] as? String, app: app,
                          isPlaying: state == "Playing")
        queue.async { [self] in
            tracks[bundleID] = state == "Stopped" && track.title == nil ? nil : track
            schedule()
        }
    }

    private func playerQuit(_ bundleID: String) {
        guard tracks.removeValue(forKey: bundleID) != nil else { return }
        schedule()
    }

    private func schedule() {
        pending += 1
        let token = pending
        queue.asyncAfter(deadline: .now() + Self.debounce) { [self] in
            if token == pending { refresh() }
        }
    }

    /// Picks the track to show (a playing one; Music's if both play) and reads its cover when it is new.
    private func refresh() {
        let candidates = [Self.musicID, Self.spotifyID].compactMap { id in tracks[id].map { (id, $0) } }
        let pick = candidates.first { $0.1.isPlaying } ?? candidates.first
        if pick?.1 != shown {
            shown = pick?.1
            publish()
        }
        guard let pick else {
            coverFor = nil
            clearColors()
            return
        }
        let (bundleID, track) = pick
        guard track.id != coverFor else { return }
        coverFor = track.id
        if bundleID == Self.musicID {
            readCover(of: track, retry: true)
        } else {
            clearColors() // Spotify: no cover without a network fetch (10-MACOS Q4)
        }
    }

    private func readCover(of track: Track, retry: Bool) {
        guard coverFor == track.id, Self.isRunning(Self.musicID) else { return }
        switch Self.musicCover() {
        case .denied:
            automationDenied = true
            clearColors()
        case .data(let data):
            automationDenied = false
            if let (pixels, width, height) = Self.decode(data) {
                let found = pixels.withUnsafeBufferPointer { buffer in
                    track.id.withCString { rl_album_art(loop.handle, buffer.baseAddress, Int32(width), Int32(height), $0) }
                }
                if found == 1 { return publish() }
            }
            fallthrough
        case .missing:
            // The cover often arrives late (doc 05 §1): keep the colours and read once more, then the manual ones.
            if retry {
                queue.asyncAfter(deadline: .now() + Self.coverRetry) { [self] in readCover(of: track, retry: false) }
            } else {
                clearColors()
            }
        }
    }

    /// The manual colours show; the window hears of it after the change, so its swatches are current.
    private func clearColors() {
        rl_album_clear(loop.handle)
        publish()
    }

    private func publish() {
        let track = shown, denied = automationDenied
        DispatchQueue.main.async { [onChange] in
            MainActor.assumeIsolated { onChange(track, denied) }
        }
    }

    // MARK: - Music's Apple events

    private enum Cover {
        case data(Data)
        case missing
        case denied
    }

    private static func isRunning(_ bundleID: String) -> Bool {
        !NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).isEmpty
    }

    private static let coverScript = NSAppleScript(source: """
        tell application id "com.apple.Music"
            if player state is stopped then return missing value
            if (count of artworks of current track) is 0 then return missing value
            return raw data of artwork 1 of current track
        end tell
        """)

    private static let trackScript = NSAppleScript(source: """
        tell application id "com.apple.Music"
            if player state is stopped then return {"", "", "stopped"}
            return {name of current track, artist of current track, player state as text}
        end tell
        """)

    private static func musicCover() -> Cover {
        var error: NSDictionary?
        guard let result = coverScript?.executeAndReturnError(&error) else {
            let code = error?[NSAppleScript.errorNumber] as? Int
            return code == -1743 ? .denied : .missing // errAEEventNotPermitted: Automation refused
        }
        let data = result.data
        return result.descriptorType == typeNull || data.isEmpty ? .missing : .data(data)
    }

    private static func musicNowPlaying() -> Track? {
        var error: NSDictionary?
        guard let result = trackScript?.executeAndReturnError(&error), result.numberOfItems == 3 else { return nil }
        let state = result.atIndex(3)?.stringValue
        guard state != "stopped" else { return nil }
        return Track(title: result.atIndex(1)?.stringValue, artist: result.atIndex(2)?.stringValue, app: "Music",
                     isPlaying: state == "playing")
    }

    /// A cover to at most 64 x 64 BGRA8 pixels, as Core's extractor takes it (doc 05 §1).
    static func decode(_ data: Data) -> ([UInt8], Int, Int)? {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceThumbnailMaxPixelSize: coverSize,
            kCGImageSourceCreateThumbnailWithTransform: true,
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else { return nil }
        let width = image.width, height = image.height
        guard width > 0, height > 0, let space = CGColorSpace(name: CGColorSpace.sRGB) else { return nil }
        var pixels = [UInt8](repeating: 0, count: width * height * 4)
        let info = CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue // BGRA
        let drawn = pixels.withUnsafeMutableBytes { buffer -> Bool in
            guard let context = CGContext(data: buffer.baseAddress, width: width, height: height, bitsPerComponent: 8,
                                          bytesPerRow: width * 4, space: space, bitmapInfo: info) else { return false }
            context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
            return true
        }
        return drawn ? (pixels, width, height) : nil
    }
}
