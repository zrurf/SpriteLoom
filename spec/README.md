# Specification

This directory contains the machine-readable specification.

- `cddl/` normative data definitions.
- `schema/` JSON Schema derived from the CDDL for tooling.
- `vectors/` conformance vectors shared by all runtimes.

The CDDL definitions are authoritative. When prose and CDDL disagree, the CDDL wins.

Each vector directory contains a `case.json` with the input, the expected result, and an optional note. Vectors are consumed by the .NET, TypeScript, and Dart runtimes.
