// Makes a throwaway update key pair for CI's appcast check: prints the private seed (Sparkle's format, base64 of 32
// bytes) and the public key, one per line. Never used for a release.
import CryptoKit

let key = Curve25519.Signing.PrivateKey()
print(key.rawRepresentation.base64EncodedString())
print(key.publicKey.rawRepresentation.base64EncodedString())
