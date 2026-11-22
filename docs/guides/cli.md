# Command line

```text
sloom <command> [options]
```

## Commands

| Command | Purpose |
| --- | --- |
| `init [path]` | Write a minimal `sloom.toml` and source directory. |
| `pack` | Build a package from the configured sources. |
| `validate` | Check configuration and source paths without packing. |
| `inspect <path>` | Print manifest counts for a directory or `.slb` bundle. |
| `keygen <prefix>` | Generate an Ed25519 key pair. |
| `keygen --algorithm aes-256 --out <key>` | Generate a 32-byte symmetric key file. |
| `encrypt <path>` | Encrypt a file or directory into `.senc` files. |
| `decrypt <path>` | Decrypt `.senc` files. |
| `sign <path>` | Sign a directory package. |
| `verify <path>` | Verify a directory package or bundle. |
| `diff <a> <b>` | Compare frame records between two packages. |
| `formats` | List image formats, texture targets, packers, and encryption algorithms. |

## Options

| Option | Applies to | Meaning |
| --- | --- | --- |
| `--config <path>` | `pack`, `validate` | Configuration file or project directory, default `sloom.toml`. |
| `--version` | all | Print the CLI version. |
| `--json` | all commands | Emit machine-readable JSON. |
| `--stats` | `inspect` | Include deduplication, utilization, animation metrics, and variant sizes. |
| `--baseline <path>` | `inspect` | Compare size figures with another package. |
| `--refresh` | `pack` | Rebuild every pass and replace cache entries. |
| `--no-cache` | `pack` | Disable the pipeline cache for this run. |
| `--watch` | `pack` | Rebuild when the configuration or sources change. |
| `--key <path>` | `sign`, `verify`, `encrypt`, `decrypt` | Private key for signing, public key for verification, or symmetric key for encryption. |
| `--decrypt-key <path>` | `inspect`, `diff`, `verify` | Symmetric key for an encrypted `.slb` bundle. |
| `--out <path>` | `keygen`, `encrypt`, `decrypt` | Output file or directory. |
| `--algorithm <name>` | `keygen`, `encrypt` | `aes-256` for key generation; `aes-256-gcm` or `xchacha20-poly1305` for encryption. |
| `--chunk-size <n>` | `encrypt` | AEAD chunk size; defaults to 65536. |
| `--permissive` | `verify` | Report unsigned extra files as warnings. |
| `--allow <path>` | `verify` | Allow an extra file or directory prefix. |

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 2 | Unknown command or invalid arguments |
| 4 | Build, validation, or verification failure |

Diagnostics use stable `SLxxxx` codes. `--json` returns them as an array with `code`, `severity`, and `message`.

## Configuration

```toml
schema = 1

[project]
id = "heroes"
name = "Heroes"

[[sources]]
id = "run"
path = "art/run"
order = "natural"
frame-duration-ms = 100

[[animations]]
uri = "anim://heroes/run"
source = "run"
loop = true
playback = "forward"

[[passes]]
id = "trim"
use = "trim.fit-pixel"
params = { threshold = 1 }

[[passes]]
id = "pack"
use = "pack.maxrects"
params = { max-size = [2048, 2048], padding = 2, pow2 = false, align = 4 }

[[passes]]
id = "bleed"
use = "bleed.page"
params = { extrude = 1, alpha-dilate = true }

[[passes]]
id = "encode"
use = "encode.page"
params = { formats = ["png", "webp", "jxl", "avif"], quality = 90 }

[[passes]]
id = "texture"
use = "texture.encode"
params = { targets = ["ktx2-astc4x4", "ktx2-bc7"], mipmaps = true, dither = "ordered4" }

[output]
kind = "directory"          # or "bundle"
path = "build/heroes"       # use build/heroes.slb for a bundle
manifest = "json"           # or "cbor"

[signing]
enabled = true
key = "keys/heroes.private.pem"

[encryption]
enabled = true
algorithm = "aes-256-gcm"    # or xchacha20-poly1305
key = "keys/asset.key"
chunk-size = 65536
```

When `kind = "bundle"`, `path` names the `.slb` file. A `[signing]` key signs both output forms during `pack`. An `[encryption]` section encrypts bundle payload blocks or directory package files. See [Encryption](encryption.md).
