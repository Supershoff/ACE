# Fidelity phase-gate corpus contract

Issue #28 is a human gate: it closes the P4 fidelity spike with executable evidence, but the actual curated corpus (real `client_portal.dat`/`client_highres.dat` extraction and real ACE appraisal captures) can only be produced by an operator on a protected runner or workstation. Nothing in this file, and no fixture file matching the patterns below, may ever be committed to this repository or attached to a GitHub issue/PR in its populated form — only redacted machine-readable pass/fail evidence (a `CloudFidelityPhaseGateReport` JSON document) may be shared.

## Running the protected harnesses

All three harnesses are ordinary MSTest tests that report `Inconclusive` (never a failure) when their corpus is not configured, so they never block ordinary CI. On a protected operator workstation, set the environment variables below and run:

```
dotnet test Source/ACE.Cloud.Worker.Tests/ACE.Cloud.Worker.Tests.csproj --filter "FullyQualifiedName~CloudFidelityPhaseGateHarnessTests"
```

| Variable | Purpose |
|---|---|
| `ACE_CLOUD_MULE_DAT_DIRECTORY` | Directory containing the operator's own `client_portal.dat` (issue #24's validated extraction path). |
| `ACE_CLOUD_MULE_ICON_FIXTURE_DIRECTORY` | Directory of `*.icon.json` files, each a `CloudIconGoldenFixture` (see below). |
| `ACE_CLOUD_MULE_APPRAISAL_CAPTURE_DIRECTORY` | Directory of `*.appraisal.json` files, each a `CloudAppraisalGoldenFixture` (see below). |
| `ACE_CLOUD_MULE_PHASE_GATE_REPORT_PATH` | Optional. If set, the combined `CloudFidelityPhaseGateReport` is also written here as JSON. |

The individual icon and appraisal harnesses (`CloudIconCompositionGoldenTests`, `CloudAppraisalGoldenCaptureComparisonTests`) can also be run independently against just their own corpus directory.

## Generating fixtures without hand-authoring JSON

Neither fixture contract needs to be hand-authored. `ACE.Cloud.Worker` ships a local-only CLI that
derives the correctly-shaped JSON from only the values an operator actually has to supply -- it never
touches a network, and the fixture files it writes stay wherever `--output-dir` points, which must never
be a path inside this repository:

```
# Icon: selected composition inputs (public DIDs/palette parameters, not private art) + a trusted
# reference PNG you have independently confirmed is correct. The tool hashes the PNG and discards its
# bytes and path -- only the hash is ever written to the fixture.
dotnet run --project Source/ACE.Cloud.Worker -- generate-icon-fixture \
  --fixture-name clothing-palette-variant-01 \
  --inputs '{"BaseIconDid":100690954,"ClothingBaseDid":33685520,"PaletteTemplate":12,"Shade":0.5}' \
  --reference-png /path/to/your/confirmed-reference.png \
  --output-dir /path/to/your/local/icon-fixtures

# Already have the hash instead of the PNG file itself? Pass --reference-hash <64 hex chars> instead of
# --reference-png.

# Appraisal: only the raw item property capture from your own successful ACE appraisal (a
# CloudAppraisalRawItemSnapshot). The tool derives the entire panel -- sections, lines, wording -- by
# running the same deterministic CloudAppraisalProjector the harness later verifies against, so you never
# write panel/section/line JSON by hand.
dotnet run --project Source/ACE.Cloud.Worker -- generate-appraisal-fixture \
  --fixture-name test-buckler \
  --capture /path/to/your/local/captured-snapshot.json \
  --output-dir /path/to/your/local/appraisal-fixtures

# Validate any existing fixture (generated or hand-edited) against the same rules the generators enforce,
# including -- for an appraisal fixture -- re-deriving ExpectedPanel and confirming it still matches:
dotnet run --project Source/ACE.Cloud.Worker -- validate-fixture /path/to/some-fixture.icon.json
```

`--inputs`/`--capture` accept either a path to a local JSON file or a literal JSON string. Every
generated fixture is checked before it is written: a fixture name containing a path separator or `..`,
a malformed reference hash, an invalid reference PNG, or any generated contract that would embed an
absolute filesystem path is rejected outright rather than written to disk.

## Icon fixture contract (`*.icon.json`)

Each file deserializes to `ACE.Cloud.Domain.CloudIconGoldenFixture`:

```json
{
  "FixtureName": "clothing-palette-variant-plate-armor",
  "Inputs": {
    "BaseIconDid": 100690954,
    "ClothingBaseDid": 33685520,
    "SetupTableId": 0,
    "PaletteTemplate": 12,
    "Shade": 0.5,
    "IgnoreCloIcons": false,
    "UnderlayDid": null,
    "OverlayDid": 100690996,
    "OverlaySecondaryDid": null,
    "ItemTypeBackgroundDid": 100667859,
    "UiEffectDids": []
  },
  "ExpectedPngSha256Hex": "<sha256 of the composed PNG bytes, lowercase hex>"
}
```

`ExpectedPngSha256Hex` is a content hash, never the image itself: a fixture file is safe to hand to an agent or commit to a private fork, because it names no DID's rendered appearance, only a hash the operator's own DAT must reproduce.

Curate fixtures covering every ASSET-005 category:

- Clothing palette/shade variants (several `PaletteTemplate`/`Shade` combinations on the same `ClothingBaseDid`).
- Underlays and overlays (including a secondary overlay).
- Tailoring (a `ClothingBaseDid` whose resolved icon differs by `SetupTableId`).
- Imbues and magical UI effects (non-empty `UiEffectDids`).
- Stack counts: Icon Reconstruction itself has no stack-count field (UI-006: "Stack quantity... remain separate UI layers"), so this category is proved by confirming a stacked and unstacked instance of the same item resolve to the identical fixture/hash, not by adding a stack input.
- Missing/corrupt references (a `BaseIconDid`/`OverlayDid` you know is absent from your DAT, or a real DID whose type is a texture but not a valid icon layer): expect the fixture to report a diagnostic-backed mismatch, and record it as an intentional negative fixture, not a bug.

## Appraisal fixture contract (`*.appraisal.json`)

Each file deserializes to `ACE.Cloud.Domain.CloudAppraisalGoldenFixture` — see that type and `CloudAppraisalRawItemSnapshot`/`CloudAppraisalPanel` for the exact shape. Curate one fixture per relevant item class (weapon, armor, wearable, consumable, currency, etc.), covering wording, colors/flags, spells, wield requirements, and special-case values a real successful ACE appraisal produces.

## Phase-gate report

`CloudFidelityPhaseGateReport` (`ACE.Cloud.Domain`) is the redacted evidence this issue's acceptance criteria ask for: one `CloudFidelityPhaseGateFixtureResult` per fixture (category, fixture name, matched, human-readable diffs — never raw bytes or filesystem paths), a `FixtureCountByCategory` coverage summary, and an explicit `NonBlockingGaps` list. A gap that is not yet covered (for example, no `client_highres.dat` corpus captured) must be named there rather than silently omitted; the phase gate only requires that every fixture that *is* included matches, not that coverage is exhaustive on the first run.

`AllPassed` additionally requires both `Icon` and `Appraisal` to be represented by at least one fixture each (`RequiredCategories`) — a run built from only an Icon corpus or only an Appraisal corpus can never pass, no matter how many fixtures it includes or how cleanly they all match. `MissingRequiredCategories` names whichever of those two categories has zero fixtures in a given run; unlike `NonBlockingGaps`, a missing required category is always blocking and is never appropriate to report as a non-blocking gap. `CloudFidelityPhaseGateHarnessTests` only reports `Inconclusive` when *neither* corpus is configured (the ordinary-CI case); an operator who configures only one corpus gets a failing test naming the missing category, not a false pass.

Only this JSON report — never the source DAT, extracted art, capture corpus, or any absolute operator path — may be attached to the GitHub issue as evidence.
