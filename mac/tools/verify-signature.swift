// Checks an update's EdDSA signature against the public key the app carries (release workflow, mac-appcast job), so a
// public key and private key that aren't a pair stop the release instead of stranding every copy built with it.
// Usage: swift mac/tools/verify-signature.swift <public key, base64> <signature, base64> <file>
import CryptoKit
import Foundation

let arguments = CommandLine.arguments
guard arguments.count == 4,
      let keyData = Data(base64Encoded: arguments[1]),
      let signature = Data(base64Encoded: arguments[2]),
      let file = FileManager.default.contents(atPath: arguments[3]),
      let key = try? Curve25519.Signing.PublicKey(rawRepresentation: keyData) else {
    FileHandle.standardError.write(Data("error: bad arguments\n".utf8))
    exit(2)
}
exit(key.isValidSignature(signature, for: file) ? 0 : 1)
