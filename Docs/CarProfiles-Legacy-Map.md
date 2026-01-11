# LalaLaunch Car Profiles JSON Schema & Legacy Field Map

## Load/save paths (JSON persistence)

### Primary storage location
- Profiles JSON file path is constructed in `ProfilesManagerViewModel` using `PluginManager.GetCommonStoragePath()` and the fixed filename `LalaLaunch_CarProfiles.json`.

### Load path
- **Method**: `ProfilesManagerViewModel.LoadProfiles()` reads the JSON file and calls `JsonConvert.DeserializeObject<ObservableCollection<CarProfile>>(json)` with no custom settings or converters; default Json.NET deserialization.
- **Call site**: `LalaLaunch` calls `ProfilesViewModel.LoadProfiles()` during startup/init to populate profiles.

### Save path
- **Method**: `ProfilesManagerViewModel.SaveProfiles()` uses `JsonConvert.SerializeObject(CarProfiles, Formatting.Indented)` with no custom settings or converters, then writes the file with `File.WriteAllText`.
- **Common call sites** (examples):
  - PB updates via `TryUpdatePBByCondition` → `SaveProfiles()`.
  - Fuel telemetry persistence and avg-lap persistence in live loop → `ProfilesViewModel?.SaveProfiles()`.
  - Pit lane loss persistence in `Pit_OnValidPitStopTimeLossCalculated` → `ProfilesViewModel?.SaveProfiles()`.
  - Planner save path in `FuelCalcs` → `_plugin.ProfilesViewModel.SaveProfiles()`.

### Serialization configuration
- Json.NET is used with default settings (no custom `JsonSerializerSettings` or converters in these calls).
- TrackStats uses `[JsonProperty]` and `[JsonIgnore]`, but there is no opt-in attribute (e.g., `[JsonObject(MemberSerialization.OptIn)]`), so **all public properties are serialized by default unless `[JsonIgnore]`**.

---

## TrackStats serialized fields table

**Legend:**
- **A** = Actively used today (read/write runtime)
- **B** = UI display derived text (ideally not serialized)
- **C** = Legacy/compatibility only (deserialize OK, avoid serialize)
- **D** = Unknown/suspicious (no clear usage found)

### A) Explicit `[JsonProperty]` fields
| Property | Class | Notes (where used) |
|---|---|---|
| DisplayName | A | Track resolution + UI/logging/selection |
| Key | A | Track identity for lookups/saves |
| BestLapMs | C | Legacy fallback only (used when dry missing) |
| BestLapMsDry | A | Live PB updates + condition selection |
| BestLapMsWet | A | Live PB updates + condition selection |
| PitLaneLossSeconds | A | Pit loss calc/logging, planner save, UI edit |
| PitLaneLossLocked | A | Gate pit-loss overwrite + UI lock |
| DryConditionsLocked | A | Prevent dry telemetry/avg updates + UI lock |
| WetConditionsLocked | A | Prevent wet telemetry/avg updates + UI lock |
| PitLaneLossBlockedCandidateSeconds | A | Stored/displayed when locked |
| PitLaneLossBlockedCandidateUpdatedUtc | A | Stored/displayed when locked |
| PitLaneLossBlockedCandidateSource | A | Stored/displayed when locked |
| DryConditionMultipliers | A | Planner/condition logic |
| WetConditionMultipliers | A | Planner/condition logic |
| PitLaneLossSource | A | Stored for UI display |
| PitLaneLossUpdatedUtc | A | Stored for UI display |
| FuelUpdatedSource | C | Legacy fallback only (dry/wet updated text) |
| FuelUpdatedUtc | C | Legacy fallback only (dry/wet updated text) |
| DryFuelUpdatedSource | A | Written by `MarkFuelUpdatedDry` + UI display |
| DryFuelUpdatedUtc | A | Written by `MarkFuelUpdatedDry` + UI display |
| WetFuelUpdatedSource | A | Written by `MarkFuelUpdatedWet` + UI display |
| WetFuelUpdatedUtc | A | Written by `MarkFuelUpdatedWet` + UI display |
| DryBestLapUpdatedSource | A | PB update metadata (dry) |
| DryBestLapUpdatedUtc | A | PB update metadata (dry) |
| WetBestLapUpdatedSource | A | PB update metadata (wet) |
| WetBestLapUpdatedUtc | A | PB update metadata (wet) |
| DryAvgLapUpdatedSource | A | Avg-lap update metadata (dry) |
| DryAvgLapUpdatedUtc | A | Avg-lap update metadata (dry) |
| WetAvgLapUpdatedSource | A | Avg-lap update metadata (wet) |
| WetAvgLapUpdatedUtc | A | Avg-lap update metadata (wet) |
| AvgFuelPerLapDry | A | Fuel calc + persistence (dry) |
| MinFuelPerLapDry | A | Fuel calc + persistence (dry) |
| MaxFuelPerLapDry | A | Fuel calc + persistence (dry) |
| DryFuelSampleCount | A | Telemetry + UI |
| AvgLapTimeDry | A | Profile pace + persistence (dry) |
| DryLapTimeSampleCount | A | Telemetry + UI |
| AvgDryTrackTemp | D | Serialized; no usage found |
| AvgFuelPerLapWet | A | Fuel calc + persistence (wet) |
| MinFuelPerLapWet | A | Fuel calc + persistence (wet) |
| MaxFuelPerLapWet | A | Fuel calc + persistence (wet) |
| WetFuelSampleCount | A | Telemetry + UI |
| AvgLapTimeWet | A | Profile pace + persistence (wet) |
| WetLapTimeSampleCount | A | Telemetry + UI |
| AvgWetTrackTemp | D | Serialized; no usage found |

### B/C/D) Public properties serialized implicitly
| Property | Class | Notes (where used) |
|---|---|---|
| BestLapMsText | C | Legacy string PB field; not bound in UI; only init in `EnsureCarTrack` |
| BestLapTimeDryText | B | UI input/display field (dry PB) |
| BestLapTimeWetText | B | UI input/display field (wet PB) |
| PitLaneLossSecondsText | B | UI input/display field (pit loss) |
| AvgFuelPerLapDryText | B | UI input/display field (dry avg fuel) |
| MinFuelPerLapDryText | B | UI input/display field (dry min fuel) |
| MaxFuelPerLapDryText | B | UI input/display field (dry max fuel) |
| AvgLapTimeDryText | B | UI input/display field (dry avg lap) |
| AvgDryTrackTempText | D | Serialized string field; no UI usage found |
| AvgFuelPerLapWetText | B | UI input/display field (wet avg fuel) |
| MinFuelPerLapWetText | B | UI input/display field (wet min fuel) |
| MaxFuelPerLapWetText | B | UI input/display field (wet max fuel) |
| AvgLapTimeWetText | B | UI input/display field (wet avg lap) |
| AvgWetTrackTempText | D | Serialized string field; no UI usage found |

---

## Legacy fields findings

### BestLapMs / BestLapMsText
- **BestLapMs exists** and is used only as a **legacy fallback** when dry/wet PBs are missing.
- PB update logic **writes BestLapMsDry/BestLapMsWet**, not BestLapMs.
- BestLapMs can still be set indirectly via `BestLapMsText` setter (manual text edits).
- `BestLapMsText` is public and not ignored, so it **is serialized by default**, but it is **not bound** in the current UI.

### FuelUpdatedSource / FuelUpdatedUtc (shared)
- Shared legacy fields still exist and are serialized.
- They are used **only as fallback** for `DryFuelLastUpdatedText`/`WetFuelLastUpdatedText` when condition-specific timestamps are missing.
- **No runtime writes** to shared fields (no call sites for `MarkFuelUpdated`).

### Dry/Wet fuel updated fields (canonical)
- `DryFuelUpdatedSource/Utc` and `WetFuelUpdatedSource/Utc` are the active paths.
- Written by `MarkFuelUpdatedDry/Wet` during telemetry persistence and planner saves.
- UI displays the derived `DryFuelLastUpdatedText` / `WetFuelLastUpdatedText`.

---

## Condition-aware data usage (dry vs wet selection)

### Best lap selection
- `GetBestLapMsForCondition(isWetEffective)` selects **Wet → Dry → BestLapMs** (wet mode) or **Dry → BestLapMs** (dry mode).
- Used in the live session loop to push PB into FuelCalcs and in FuelCalcs to update PB display based on `IsWet`.

### Average lap time selection
- Live session baseline: `GetProfileAvgLapSeconds()` **prefers dry**, falls back to wet.
- FuelCalcs planning: `GetProfileLapTimeForCondition` uses **wet/dry depending on `isWet`**, with wet fallback computed from dry × wet factor.

### Fuel min/avg/max selection
- Profile min/max: **dry uses dry values**, **wet uses wet values** with **dry × wet factor fallback**.
- Average fuel for planning: same wet/dry preference with fallback.

### Seed/baseline pace used by Pit/Fuel systems
- PitLite baseline pace uses **live average first**, then **profile AvgLapTimeDry** (no wet handling in fallback).
- Fuel planner baseline pace uses `GetProfileLapTimeForCondition` (condition-aware with wet scaling), and live pace where available.

---

## Risks / recommendations for follow-up change task (facts only)

- If **BestLapMs** is no longer serialized, legacy PB display may go blank when `BestLapMsDry` is missing because fallbacks depend on it, and improvement checks use `BestLapMsDry ?? BestLapMs`.
- If **FuelUpdatedSource/Utc** are not serialized, legacy profiles lacking dry/wet metadata will show **blank “last updated”** text because dry/wet fallbacks rely on shared values.
- If **BestLapMsText** stops serializing, older JSONs that only saved text PBs will lose that data unless migrated.

