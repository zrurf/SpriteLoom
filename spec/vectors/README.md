# Conformance Vectors

Each vector directory contains a `case.json`:

```json
{
  "name": "iri.normalize.example",
  "kind": "iri.normalize",
  "input": { "iri": "frame://heroes/run/0001" },
  "expected": { "iri": "frame://heroes/run/0001" }
}
```

Kinds:

- `iri.normalize`: normalize `input.iri` and compare with `expected.iri`.
- `iri.reject`: reject `input.iri` with `expected.code`.

Vectors are shared by the .NET, TypeScript, and Dart runtimes. A new vector must pass in all runtimes before it is accepted.
