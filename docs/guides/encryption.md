# Encryption

SpriteLoom supports authenticated encryption for `.senc` files and for `.slb` bundle payload blocks. Encryption is a confidentiality layer, not DRM: anyone who can run the client can obtain the key.

## Algorithms

| Name | Properties |
| --- | --- |
| `aes-256-gcm` | 256-bit AES-GCM, 12-byte nonce, 16-byte tag. |
| `xchacha20-poly1305` | 256-bit XChaCha20-Poly1305, 24-byte nonce, 16-byte tag. |

Both modes use the same 32-byte symmetric key and a 16-byte authentication tag per chunk.

## Keys

```bash
sloom keygen --algorithm aes-256 --out keys/asset.key
```

The generated file contains 64 lowercase hex characters plus a newline. `SymmetricKey.Load` also accepts a 32-byte raw key file. On Unix, `SymmetricKey.Save` sets mode `0600`.

The key id is the low 16 bits of the `xxh3-64` digest of the key and is stored in each encrypted artifact. It is a lookup hint, not a secret.

## `.senc` files

Encrypt one file or a directory tree:

```bash
sloom encrypt assets/hero.png --key keys/asset.key --algorithm aes-256-gcm
sloom encrypt build/heroes --key keys/asset.key --out build/heroes.encrypted
```

Decrypt:

```bash
sloom decrypt assets/hero.png.senc --key keys/asset.key
sloom decrypt build/heroes.encrypted --key keys/asset.key --out build/heroes.restored
```

A single file defaults to `<name>.senc`; a directory defaults to `<name>.encrypted` / `<name>.decrypted` and mirrors the tree. `--chunk-size` defaults to 65536 bytes. AEAD is applied after any user-level compression; `.senc` itself does not compress.

Header layout:

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | magic `SENC` |
| 4 | 1 | version `1` |
| 5 | 1 | algorithm (`1` AES-256-GCM, `2` XChaCha20-Poly1305) |
| 6 | 2 | key id, little-endian |
| 8 | 4 | chunk size, little-endian |
| 12 | 16 | nonce prefix storage |
| 28 | 4 | chunk count, little-endian |
| 32 | 4 | reserved, zero |

Each chunk uses a nonce made from the algorithm-specific prefix and a little-endian chunk index. The tag is appended to each chunk ciphertext. There is no whole-file padding.

## Encrypted bundles

Add an `[encryption]` section to `sloom.toml`:

```toml
[encryption]
enabled = true
algorithm = "xchacha20-poly1305"
key = "keys/asset.key"
chunk-size = 65536

[output]
kind = "bundle"
path = "build/heroes.slb"
manifest = "cbor"
```

`sloom pack` loads the key relative to the project directory. With `kind = "bundle"` every payload block is encrypted independently; with `kind = "directory"` every package file is stored as a `.senc` sidecar and the original logical paths remain in the manifest. The manifest, containers, images, animations, and blobs are covered. Bundle signature blocks are never encrypted.

Bundle details:

- Block keys are derived with `HKDF-SHA256(master, salt, "slb-block" || key-id || block-id)`.
- The index `crypto` entry stores the algorithm, key id, salt, nonce prefix, and chunk size.
- Block entries carry `ENCRYPTED`, `chunk-count`, and `key-id`; `raw-size` is the plaintext length.
- Digests and signatures are computed over plaintext, so adding encryption does not invalidate an existing signature.
- Salt and nonce prefixes are derived deterministically from the key and plaintext content. Rebuilding the same inputs with the same key produces the same bundle; changing content changes the nonce inputs.

Reading an encrypted bundle requires the symmetric key:

```csharp
var key = SymmetricKey.Load("keys/asset.key");
var archive = SlbReader.ReadFile("build/heroes.slb", decryptionKey: key);
```

Without a key, the reader reports `SL5022`. A key with the wrong id reports `SL5043`, and a failed authentication tag reports `SL5045`.

Encrypted directory packages use the same `SENC` container per file, so readers must decrypt `.senc` entries before parsing the manifest. CLI commands accept `--decrypt-key`:

```bash
sloom inspect build/heroes.slb --decrypt-key keys/asset.key --stats
sloom verify build/heroes.slb --key keys/publisher.public.pem --decrypt-key keys/asset.key
```

## Current limits

- Raw 32-byte keys only; passphrase/Argon2id key derivation is not exposed by the CLI yet.
- The GUI does not yet prompt for a symmetric key, so encrypted directory packages are currently a CLI/API feature.
- Encrypted bundles cannot be inspected, diffed, or signature-verified without the symmetric key because digests and signatures cover plaintext. Encrypted directory signatures cover the stored `.senc` files, so their signature verification does not require the key.
- Encryption does not replace signing. Signing authenticates the publisher; encryption protects payload confidentiality.
- Reusing a key across unrelated projects is allowed but not recommended.
