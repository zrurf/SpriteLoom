# Identifiers

SpriteLoom addresses every stored object with an absolute IRI. The scheme identifies the object kind; the authority is the package id; the path is the object path inside that package.

| Scheme | Object |
| --- | --- |
| `pkg:` | Package root |
| `frame:` | Frame record |
| `anim:` | Animation document |
| `page:` | Atlas page |
| `blob:` | Arbitrary binary payload |

## Syntax

```
scheme "://" authority "/" path
```

Examples:

```text
pkg://heroes
frame://heroes/run/a1
frame://heroes/café/idle
anim://heroes/run
page://heroes/0000
blob://heroes/licenses/font.txt
```

## Normalization

Identifiers are stored in normalized form:

- Unicode is normalized to NFC.
- Scheme and authority are lowercase.
- Percent-encoded octets are decoded when they represent an unreserved or international character, then re-encoded only when required.
- Query (`?`) and fragment (`#`) components are not permitted.
- The normalized form is limited to 512 UTF-8 bytes.
- Private-use code points are rejected.

Two identifiers are equal only when their normalized strings are equal. Implementations must not compare raw input strings.

## Frame identity

A frame IRI is derived from the source file-name stem, not from the full path or a generated sequence number. Importing `art/run/a10.png` from source `run` in package `heroes` produces:

```text
frame://heroes/run/a10
```

The stem rule keeps identifiers stable when files are reordered or moved between directories. Duplicate stems inside one source are rejected rather than silently disambiguated.
