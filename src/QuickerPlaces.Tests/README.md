# QuickerPlaces.Tests

xUnit tests for services and models only (D5 in `ai/260901_Phase 1 Detailed
Plan.md`). No test constructs a `Window`, so no test needs an STA thread or
a message pump — the default xUnit test runner is enough.

`Fixtures/places.v1.json` is read via `AppContext.BaseDirectory` (not as an
embedded resource) — see `QuickerPlaces.Tests.csproj`'s `CopyToOutputDirectory`
item and `PlacesStoreFixtureTests`. That fixture is frozen once written.
