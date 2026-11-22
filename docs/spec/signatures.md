# Signatures

Packages may be signed with Ed25519. Verification is offline: only the public key is required, and private keys never enter a package.

## Keys

```bash
sloom keygen keys/heroes
```

This writes:

```text
keys/heroes.private.pem   PKCS#8 private key
keys/heroes.public.pem    SubjectPublicKeyInfo public key
```

The key id is the lowercase hex encoding of the `xxh3-64` digest of the encoded public key.

## Directory packages

A directory signature covers every file except `signatures/`:

- Paths are relative to the package root and use `/`.
- Each file is recorded with its size and SHA-256 digest.
- The signature document is stored as `signatures/<key-id>.sig.cbor`.

Verification checks the Ed25519 signature first, then each recorded file. Encrypted bundle payloads must be decrypted before signature verification because the signature covers plaintext; use the bundle decryption key. Strict mode rejects files that are not covered by the signature. Permissive mode reports them as warnings. Explicit whitelist entries allow named files or directory prefixes.

## Bundles

A bundle signature covers the package structure, not just individual files:

- `merkle-root` is the BLAKE3 Merkle root over payload block contents in block-id order.
- `block-table-digest` is BLAKE3 over the payload block entries, excluding the signature block.
- `index-digest` is BLAKE3 over the complete canonical CBOR index, including the signature path entry.

The signature document itself is an ordinary block with kind `signature`. The header `Signed` flag is set. Verification rejects a bundle when the signature is absent, the signer key does not match, the Merkle root differs, or either digest differs.

## Signature document

The document is canonical CBOR with these keys:

| Key | Meaning |
| --- | --- |
| `format` | `sprite-loom/signature` |
| `format-major` | `1` |
| `layer` | `file` or `bundle` |
| `merkle-root` | Optional BLAKE3 root |
| `block-table-digest` | Optional digest object |
| `index-digest` | Optional digest object |
| `files` | Optional array of path, size, digest |
| `signers` | Array of key id, algorithm, public key, signature |

The signature is computed over the document with the `signature` field omitted. Adding signatures therefore does not change the signed bytes.

## Commands

```bash
sloom sign build/heroes --key keys/heroes.private.pem
sloom verify build/heroes --key keys/heroes.public.pem
sloom verify build/heroes.slb --key keys/heroes.public.pem
```

`verify` uses strict mode by default. Use `--permissive` or `--allow <path>` to accept extra unsigned files.

## Diagnostic codes

| Code | Meaning |
| --- | --- |
| `SL5001` | Signature block missing |
| `SL5002` | Requested signer key missing |
| `SL5003` | Ed25519 signature invalid |
| `SL5004` | Block table digest mismatch |
| `SL5005` | Index digest mismatch |
| `SL5006` | Merkle root mismatch |
| `SL5007` | Signed file missing |
| `SL5008` | Signed file size mismatch |
| `SL5009` | Signed file digest mismatch |
| `SL5010` | Unsigned file present |
| `SL5011` | Signature file missing |
| `SL5023` | Header CRC mismatch |
| `SL5029` | Index CRC mismatch |
| `SL5035` | Block digest mismatch |
