# LalaLaunch Car Profiles JSON Schema & Legacy Field Map

Validated against commit: 298accf  
Last updated: 2026-02-10  
Branch: work

## Load/save paths (JSON persistence)

### Primary storage location (current)
- Profiles JSON file path is constructed in `ProfilesManagerViewModel` via `PluginStorage.GetPluginFilePath("CarProfiles.json")`.
- This resolves to `PluginsData/Common/LalaPlugin/CarProfiles.json` under SimHub’s base directory.

### Legacy location (migrated on load)
- Legacy file name: `LalaLaunch_CarProfiles.json` in the common storage root (`PluginStorage.GetCommonFilePath`).
- On load, `ProfilesManagerViewModel.LoadProfiles()` calls `PluginStorage.TryMigrate(...)` to copy legacy data into the new `LalaPlugin` folder when needed.

### Load path
- **Method**: `ProfilesManagerViewModel.LoadProfiles()` first tries to deserialize a `CarProfilesStore` wrapper (schema version + profiles). If that fails, it falls back to deserializing an `ObservableCollection<CarProfile>` directly.
- **Sanitization**: during load, any `BaseTankLitres` values that are `NaN`, `∞`, or ≤0 are cleared.

### Save path
- **Method**: `ProfilesManagerViewModel.SaveProfiles()` always writes a `CarProfilesStore` wrapper with `SchemaVersion = 2` and the `Profiles` list.
- **Serialization**: `JsonConvert.SerializeObject(store, Formatting.Indented)` writes to the new path, creating the folder if needed.

---

## Schema container (CarProfilesStore)
- `CarProfilesStore` is opt-in serialized (`[JsonObject(MemberSerialization.OptIn)]`).
- Fields:
  - `SchemaVersion` (int, current value `2`).
  - `Profiles` (array of `CarProfile`).

---

## CarProfile serialization rules
- `CarProfile` **does not** declare opt-in serialization, so all public properties are serialized by default unless `[JsonIgnore]` is added.
- The car-level schema therefore includes the full launch, fuel, dash, and pit-entry settings alongside track stats.
- New car-level fields introduced in this range of PRs:
  - `BaseTankLitres` (nullable double) — optional base tank value recorded per car profile.
  - `DryConditionMultipliers` / `WetConditionMultipliers` — per-car condition overrides, serialized via `[JsonProperty]`.

---

## Track key normalization
- Track keys are canonicalized in `CarProfile` using `Trim().ToLowerInvariant()` before lookup/storage.
- `EnsureTrack(...)` rewrites dictionary keys to the canonical form and updates `DisplayName`/`Key` on the record.

---

## TrackStats serialization (Schema v2, opt-in)
`TrackStats` now uses `[JsonObject(MemberSerialization.OptIn)]`, so **only** fields explicitly marked `[JsonProperty]` are saved. UI-only text fields and computed deltas are **not** persisted.

### A) Identity + locks
| Field | Notes |
| --- | --- |
| `DisplayName` | Track display label shown in UI. |
| `Key` | Canonical track key (lowercase). |
| `PitLaneLossLocked` | Prevents automatic pit-loss overwrite. |
| `DryConditionsLocked` / `WetConditionsLocked` | Prevents auto-updates from telemetry for the condition. |

### B) Pit lane loss data
| Field | Notes |
| --- | --- |
| `PitLaneLossSeconds` | Current pit-lane loss (seconds). |
| `PitLaneLossSource` / `PitLaneLossUpdatedUtc` | Source + timestamp shown in the UI. |
| `PitLaneLossBlockedCandidateSeconds` / `PitLaneLossBlockedCandidateSource` / `PitLaneLossBlockedCandidateUpdatedUtc` | Stored when a locked pit-loss blocks a new candidate. |

### C) Condition multipliers
| Field | Notes |
| --- | --- |
| `DryConditionMultipliers` / `WetConditionMultipliers` | Per-track condition overrides (fuel + refuel math). |

### D) Best lap (per condition)
| Field | Notes |
| --- | --- |
| `BestLapMsDry` / `BestLapMsWet` | Best lap times in milliseconds (dry/wet). |
| `DryBestLapUpdatedSource` / `DryBestLapUpdatedUtc` | Metadata for dry PB edits/telemetry. |
| `WetBestLapUpdatedSource` / `WetBestLapUpdatedUtc` | Metadata for wet PB edits/telemetry. |

### E) Average lap time (per condition)
| Field | Notes |
| --- | --- |
| `AvgLapTimeDry` / `AvgLapTimeWet` | Avg lap times in milliseconds (dry/wet). |
| `DryLapTimeSampleCount` / `WetLapTimeSampleCount` | Sample counts driving averages. |
| `DryAvgLapUpdatedSource` / `DryAvgLapUpdatedUtc` | Metadata for dry avg lap persistence. |
| `WetAvgLapUpdatedSource` / `WetAvgLapUpdatedUtc` | Metadata for wet avg lap persistence. |

### F) Fuel burn (per condition)
| Field | Notes |
| --- | --- |
| `AvgFuelPerLapDry` / `AvgFuelPerLapWet` | Average fuel burn per lap. |
| `MinFuelPerLapDry` / `MinFuelPerLapWet` | Minimum recorded burn per lap. |
| `MaxFuelPerLapDry` / `MaxFuelPerLapWet` | Maximum recorded burn per lap. |
| `DryFuelSampleCount` / `WetFuelSampleCount` | Sample counts driving averages. |
| `DryFuelUpdatedSource` / `DryFuelUpdatedUtc` | Metadata for dry fuel updates. |
| `WetFuelUpdatedSource` / `WetFuelUpdatedUtc` | Metadata for wet fuel updates. |

---

## Legacy compatibility notes
- **Legacy wrapper-less JSON** is still accepted. Load falls back to `ObservableCollection<CarProfile>` if the new wrapper fails to deserialize.
- **Unknown legacy fields** (e.g., old `BestLapMs`, `FuelUpdatedSource`, text-based UI fields) are ignored when deserializing into the schema v2 `TrackStats` because of opt-in serialization.
- **Hydration suppression**: `_isHydrating` prevents log spam on initial deserialization; update logs fire only after load completes.

---

## Practical guidance
- When editing JSON manually, use **lowercase track keys** to avoid duplicate records.
- Use the per-condition metadata fields (`Dry*` / `Wet*`) instead of legacy shared timestamps.
- If you migrate from older JSON exports, expect any UI text-only fields to be dropped once saved back in schema v2.
