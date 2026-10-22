import AppKit
import CRimlight
import ImageIO
import os

/// What's playing, and the album colours (docs/10-MACOS.md §4.2). Music and Spotify announce track changes with
/// distributed notifications, which need no permission. The cover is read with an Apple event, only on a track change
/// and only while that player is running (an Apple event to a player that isn't running would launch it); that asks
/// once per player for Automation permission ("control Music", "control Spotify"). Music hands over the image itself.
/// Spotify hands over only its address on Spotify's image server, so that one image is downloaded (Maxwell allowed it,
/// Q4): from Spotify's own image hosts only, over HTTPS, with no cookies or cache, nothing else sent. Every other player
/// gets the manual colours.
///
/// Doc 05 §1's rules: a change is read 250 ms after the last event; a missing cover keeps the previous colours and is
/// read once more after 750 ms, then the manual colours show. The state lives on one serial queue; the Apple events run
/// on a second one (they can wait on Music, or on the user answering the Automation prompt) with a 3 s timeout, so
/// notifications never queue up behind them. Covers are read only while album colours are in use, so a user on manual
/// colours is never asked about Automation. `onChange` is called on the main thread.
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
    private static let maxCoverBytes = 4 << 20

    /// One ephemeral session for Spotify's covers: no cookies, no disk cache, no credentials.
    private static let session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.httpShouldSetCookies = false
        configuration.httpCookieAcceptPolicy = .never
        configuration.urlCache = nil
        configuration.timeoutIntervalForRequest = 5
        configuration.timeoutIntervalForResource = 10
        return URLSession(configuration: configuration)
    }()
    private static let debounce = 0.25, coverRetry = 0.75
    private static let coverSize = 64 // doc 05 §1: decode to at most 64 x 64

    private let loop: CoreLoop
    private let queue = DispatchQueue(label: "media", qos: .utility)
    private let events = DispatchQueue(label: "media.appleevents", qos: .utility) // one NSAppleScript at a time
    private let log = Logger(subsystem: Bundle.main.bundleIdentifier ?? "app", category: "media")
    private let onChange: @MainActor (Track?, Bool) -> Void
    private var observers: [NSObjectProtocol] = []

    // Queue only:
    private var tracks: [String: Track] = [:] // the latest from each player, by bundle ID
    private var shown: Track?
    private var coverFor: String?           // the track whose cover was last read
    private var pending = 0
    private var automationDenied = false     // the shown player refused Automation
    private var deniedBy: String?             // which one
    private var albumColorsWanted = false

    /// `onChange(track, automationDenied)`: the track to show (nil: none) and whether its player refused the cover.
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
            guard let self, let id = app?.bundleIdentifier else { return }
            queue.async { self.playerQuit(id) }
        })
    }

    /// Whether the glow uses album colours (Color mode "From album art" without "Use my colors"). Covers are read only
    /// then; turning it on reads the current one. Any thread.
    func setAlbumColorsWanted(_ wanted: Bool) {
        queue.async { [self] in
            guard wanted != albumColorsWanted else { return }
            albumColorsWanted = wanted
            guard wanted else { return }
            if tracks[Self.musicID] == nil {
                // What Music plays already (Spotify announces itself on its next change).
                events.async { [self] in
                    let track = Self.musicNowPlaying()
                    queue.async { [self] in
                        if let track, tracks[Self.musicID] == nil { tracks[Self.musicID] = track }
                        coverFor = nil
                        schedule()
                    }
                }
            } else {
                coverFor = nil
                schedule()
            }
        }
    }

    /// Reads the cover again (after the user may have allowed Automation in System Settings). Any thread.
    func recheck() {
        queue.async { [self] in
            guard automationDenied else { return }
            coverFor = nil
            schedule()
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
        var changed = pick?.1 != shown
        shown = pick?.1
        if automationDenied && pick?.0 != deniedBy {
            automationDenied = false // the warning is about the player that refused
            changed = true
        }
        if changed { publish() }
        guard let pick else {
            coverFor = nil
            clearColors()
            return
        }
        let (bundleID, track) = pick
        guard track.id != coverFor, albumColorsWanted else { return }
        coverFor = track.id
        readCover(of: track, from: bundleID, retry: true)
    }

    private func readCover(of track: Track, from bundleID: String, retry: Bool) {
        guard coverFor == track.id, albumColorsWanted, Self.isRunning(bundleID) else { return }
        events.async { [self] in
            if bundleID == Self.musicID {
                let cover = Self.musicCover(title: track.title, log: log)
                queue.async { [self] in useCover(cover, of: track, from: bundleID, retry: retry) }
                return
            }
            switch Self.spotifyCoverURL(title: track.title, log: log) {
            case .denied:
                queue.async { [self] in useCover(.denied, of: track, from: bundleID, retry: retry) }
            case .missing:
                queue.async { [self] in useCover(.missing, of: track, from: bundleID, retry: retry) }
            case .url(let url):
                Self.download(url, log: log) { [self] data in
                    queue.async { [self] in
                        useCover(data.map(Cover.data) ?? .missing, of: track, from: bundleID, retry: retry)
                    }
                }
            }
        }
    }

    private func useCover(_ cover: Cover, of track: Track, from bundleID: String, retry: Bool) {
        guard coverFor == track.id else { return } // another track meanwhile
        switch cover {
        case .denied:
            automationDenied = true
            deniedBy = bundleID
            coverFor = nil // read again once allowed (recheck)
            clearColors()
        case .data(let data):
            automationDenied = false
            if let (pixels, width, height) = Self.decode(data) {
                let found = pixels.withUnsafeBufferPointer { buffer in
                    track.id.withCString { rl_album_art(loop.handle, buffer.baseAddress, Int32(width), Int32(height), $0) }
                }
                if found == 1 { return publish() }
                log.info("The cover has no opaque pixel: the manual colors show")
            } else {
                log.notice("Music's cover (\(data.count) bytes) couldn't be decoded")
            }
            fallthrough
        case .missing:
            // The cover often arrives late (doc 05 §1): keep the colours and read once more, then the manual ones.
            if retry {
                queue.asyncAfter(deadline: .now() + Self.coverRetry) { [self] in
                    readCover(of: track, from: bundleID, retry: false)
                }
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

    // Both scripts first ask whether Music runs, which never launches it (a `tell` would, if it quit meanwhile), and
    // give up after 3 s rather than AppleScript's default 2 minutes.
    private static let coverScript = NSAppleScript(source: """
        if application id "com.apple.Music" is not running then return missing value
        with timeout of 3 seconds
            tell application id "com.apple.Music"
                if player state is stopped then return missing value
                if (count of artworks of current track) is 0 then return missing value
                set art to artwork 1 of current track
                try
                    set bytes to raw data of art
                on error
                    set bytes to data of art -- some streamed tracks have no raw data
                end try
                return {name of current track, bytes}
            end tell
        end timeout
        """)

    private static let trackScript = NSAppleScript(source: """
        if application id "com.apple.Music" is not running then return missing value
        with timeout of 3 seconds
            tell application id "com.apple.Music"
                if player state is stopped then return {"", "", "stopped"}
                return {name of current track, artist of current track, player state as text}
            end tell
        end timeout
        """)

    /// Music's current cover, if it still plays `title` (a skip during the read would give the next song's).
    private static func musicCover(title: String?, log: Logger) -> Cover {
        var error: NSDictionary?
        guard let result = coverScript?.executeAndReturnError(&error) else {
            let code = error?[NSAppleScript.errorNumber] as? Int
            let message = error?[NSAppleScript.errorMessage] as? String ?? ""
            log.notice("Music's cover couldn't be read: \(code ?? 0) \(message, privacy: .public)")
            return code == -1743 ? .denied : .missing // errAEEventNotPermitted: Automation refused
        }
        guard result.numberOfItems == 2, let data = result.atIndex(2)?.data, !data.isEmpty else {
            log.info("Music has no cover for this track")
            return .missing
        }
        if let title, let playing = result.atIndex(1)?.stringValue, playing != title { return .missing }
        return .data(data)
    }

    private static func musicNowPlaying() -> Track? {
        var error: NSDictionary?
        guard let result = trackScript?.executeAndReturnError(&error), result.numberOfItems == 3 else { return nil }
        let state = result.atIndex(3)?.stringValue
        guard state != "stopped" else { return nil }
        return Track(title: result.atIndex(1)?.stringValue, artist: result.atIndex(2)?.stringValue, app: "Music",
                     isPlaying: state == "playing")
    }

    // MARK: - Spotify's cover

    private enum CoverURL {
        case url(URL)
        case missing
        case denied
    }

    private static let spotifyScript = NSAppleScript(source: """
        if application id "com.spotify.client" is not running then return missing value
        with timeout of 3 seconds
            tell application id "com.spotify.client"
                if player state is stopped then return missing value
                return {name of current track, artwork url of current track}
            end tell
        end timeout
        """)

    /// The address of Spotify's current cover, if it still plays `title`.
    private static func spotifyCoverURL(title: String?, log: Logger) -> CoverURL {
        var error: NSDictionary?
        guard let result = spotifyScript?.executeAndReturnError(&error) else {
            let code = error?[NSAppleScript.errorNumber] as? Int
            let message = error?[NSAppleScript.errorMessage] as? String ?? ""
            log.notice("Spotify's cover couldn't be read: \(code ?? 0) \(message, privacy: .public)")
            return code == -1743 ? .denied : .missing
        }
        guard result.numberOfItems == 2, let text = result.atIndex(2)?.stringValue,
              let url = URL(string: text), isSpotifyImage(url) else {
            log.info("Spotify has no cover address for this track")
            return .missing
        }
        if let title, let playing = result.atIndex(1)?.stringValue, playing != title { return .missing }
        return .url(url)
    }

    /// Only Spotify's own image hosts, over HTTPS: the address comes from another app, so nothing else is fetched.
    static func isSpotifyImage(_ url: URL) -> Bool {
        guard url.scheme == "https", let host = url.host?.lowercased(), url.user == nil else { return false }
        return host == "i.scdn.co" || host.hasSuffix(".scdn.co") || host.hasSuffix(".spotifycdn.com")
    }

    /// Downloads one cover; nil on any failure. Calls back on a URLSession queue.
    private static func download(_ url: URL, log: Logger, done: @escaping @Sendable (Data?) -> Void) {
        var request = URLRequest(url: url)
        request.httpShouldHandleCookies = false
        session.dataTask(with: request) { data, response, error in
            let status = (response as? HTTPURLResponse)?.statusCode ?? 0
            guard error == nil, status == 200, let data, !data.isEmpty, data.count <= NowPlayingService.maxCoverBytes else {
                log.notice("Spotify's cover couldn't be downloaded (HTTP \(status))")
                return done(nil)
            }
            done(data)
        }.resume()
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
