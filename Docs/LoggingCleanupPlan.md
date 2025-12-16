# Logging Cleanup Plan

## Part A — Inventory with Recommendations

| File | Method / Context | Level | Current Message | Trigger | Frequency | Prod-safe INFO? | Action | New Prefix | Proposed Template |
|---|---|---|---|---|---|---|---|---|---|
|ProfilesManagerViewModel.cs|TryUpdatePB|Info|"[PB] Reject: missing car/track."|PB update attempt without car/track|Per lap candidate|Yes|REWRITE|[LalaLaunch:LAP]|"PB reject reason=missing_car_track"|
|ProfilesManagerViewModel.cs|TryUpdatePB|Info|"[PB] Reject: out of range ({lapMs} ms)."|Lap MS outside bounds|Per lap candidate|Yes|REWRITE|[LalaLaunch:LAP]|"PB reject reason=lap_ms_range lap_ms={lapMs}"|
|ProfilesManagerViewModel.cs|TryUpdatePB|Info|"[PB] Reject: not improved enough. old={ts.BestLapMs} new={lapMs} (≥{PB_IMPROVE_MS} ms required)."|Lap slower than threshold|Per lap candidate|Yes|REWRITE|[LalaLaunch:LAP]|"PB reject reason=insufficient_gain old_ms={ts.BestLapMs} new_ms={lapMs} min_gain_ms={PB_IMPROVE_MS}"|
|ProfilesManagerViewModel.cs|TryUpdatePB|Info|"[PB] Updated: {carName} @ '{ts.DisplayName}' -> {ts.BestLapMsText}"|PB accepted and saved|Per lap accepted|Yes|REWRITE|[LalaLaunch:LAP]|"PB updated car={carName} track={ts.DisplayName} lap={ts.BestLapMsText}"|
|ProfilesManagerViewModel.cs|EnsureCar|Info|"[Profiles] EnsureCar('{carProfileName}') -> FOUND existing profile."|Ensure profile already exists|On profile lookup|Yes|DOWNGRADE|[LalaLaunch:PROFILE]|"Profile ensure car={carProfileName} status=found"|
|ProfilesManagerViewModel.cs|EnsureCar|Info|"[Profiles] EnsureCar('{carProfileName}') -> CREATED new profile."|New car profile creation|Per missing car|Yes|KEEP|[LalaLaunch:PROFILE]|"Profile ensure car={carProfileName} status=created"|
|ProfilesManagerViewModel.cs|EnsureCarTrack|Info|"[Profiles] EnsureCarTrack('{carProfileName}', '{trackName}')"|Ensure car track invoked|Per call|Yes|DOWNGRADE|[LalaLaunch:PROFILE]|"Profile ensure_track car={carProfileName} track={trackName}"|
|ProfilesManagerViewModel.cs|EnsureCarTrack|Info|"[Profiles] Track resolved -> Key='{ts?.Key}', Disp='{ts?.DisplayName}'"|Track resolved after ensure|Per call|Yes|KEEP|[LalaLaunch:PROFILE]|"Profile track_resolved car={carProfileName} track_key={ts.Key} track_name={ts.DisplayName}"|
|ProfilesManagerViewModel.cs|EnsureCarTrack-UI pre|Info|"[Profiles][UI] Before refresh: SelectedProfile='{SelectedProfile?.ProfileName}', targetCar='{car.ProfileName}'"|Before refreshing track list|UI action|Yes|KEEP|[LalaLaunch:TRACE]|"UI track_refresh stage=before selected={SelectedProfile?.ProfileName} target={car.ProfileName}"|
|ProfilesManagerViewModel.cs|EnsureCarTrack-UI post|Info|"[Profiles][UI] After refresh: TracksForSelectedProfile.Count={TracksForSelectedProfile?.Count ?? 0}, keys=[{string.Join(",", keysAfter)}]"|After refreshing list|UI action|Yes|KEEP|[LalaLaunch:TRACE]|"UI track_refresh stage=after count={TracksForSelectedProfile?.Count} keys=[...]"|
|ProfilesManagerViewModel.cs|EnsureCarTrack-UI lookup|Info|"[Profiles][UI] Looking for track: key='{ts?.Key}', name='{ts?.DisplayName}'"|Before selecting track|UI action|Yes|KEEP|[LalaLaunch:TRACE]|"UI track_select search_key={ts.Key} search_name={ts.DisplayName}"|
|ProfilesManagerViewModel.cs|EnsureCarTrack-UI missing|Info|"[Profiles][UI] Track instance not found in TracksForSelectedProfile after refresh."|Track not found post-refresh|Rare error|Yes|REWRITE|[LalaLaunch:WARN]|"UI track_select missing track_key={ts.Key} track_name={ts.DisplayName}"|
|ProfilesManagerViewModel.cs|LoadProfiles|Error|"LalaLaunch: Failed to load car profiles: {ex.Message}"|Exception reading profiles file|Session start|Yes|KEEP|[LalaLaunch:ERROR]|"Profile load failed err={ex.Message}"|
|ProfilesManagerViewModel.cs|LoadProfiles|Info|"[Profiles] 'Default Settings' profile not found – creating baseline profile."|Default profile missing|Startup rare|Yes|KEEP|[LalaLaunch:PROFILE]|"Profile default missing action=create"|
|ProfilesManagerViewModel.cs|SaveProfiles|Info|"[Profiles] All car profiles saved to JSON file."|Successful save|Per save|Yes|DOWNGRADE|[LalaLaunch:PROFILE]|"Profile save result=success"|
|ProfilesManagerViewModel.cs|SaveProfiles|Error|"LalaLaunch: Failed to save car profiles: {ex.Message}"|Exception saving|Per failure|Yes|KEEP|[LalaLaunch:ERROR]|"Profile save result=error err={ex.Message}"|
|LalaLaunch.cs|SaveChanges|Info|"[Profiles] Changes to '{ActiveProfile?.ProfileName}' saved."|Manual save|User action|Yes|KEEP|[LalaLaunch:PROFILE]|"Profile save active={ActiveProfile?.ProfileName}"|
|LalaLaunch.cs|CaptureFuelSeedForNextSession|Info|"[LiveFuel] CaptureFuelSeedForNextSession error: {ex.Message}"|Exception during capture|Rare|Yes|REWRITE|[LalaLaunch:ERROR]|"Fuel seed capture failed err={ex.Message}"|
|LalaLaunch.cs|TryUpdateFuelModel (loop)|Info|"[LiveFuel] Car/track change detected – clearing seeds and confidence"|Car/track change|Per change|Yes|KEEP|[LalaLaunch:FUEL]|"Fuel seed reset reason=car_track_change"|
|LalaLaunch.cs|HandleSessionChangeForFuelModel|Info|"[LiveFuel] HandleSessionChangeForFuelModel error: {ex.Message}"|Fuel session change exception|Rare|Yes|REWRITE|[LalaLaunch:ERROR]|"Fuel session change failed err={ex.Message}"|
|LalaLaunch.cs|LapDetector low speed|Info|"[LapDetector] Ignoring lap increment at low speed ({speedKmh:F1} km/h)."|Lap increment ignored low speed|Per lap edge|Yes|KEEP|[LalaLaunch:LAP]|"Lap increment ignored reason=low_speed speed_kmh={speedKmh:F1}"|
|LalaLaunch.cs|LapDetector track% anomaly|Info|"[LapDetector] Lap increment detected via CompletedLaps with atypical track% values (last={lastPctNormalized:F3}, cur={curPctNormalized:F3})."|Lap count jump detection|Per lap|Yes|KEEP|[LalaLaunch:LAP]|"Lap increment source=completed_laps last_pct={lastPctNormalized:F3} cur_pct={curPctNormalized:F3}"|
|LalaLaunch.cs|Log pace+fuel summary|Info|"{prefix} | {pacePart} | {fuelPart}"|Lap summary logging|Per lap|Yes|REWRITE|[LalaLaunch:LAP]|"Lap summary prefix={prefix} pace={pacePart} fuel={fuelPart}"|
|LalaLaunch.cs|Pace fallback|Info|"[Pace] No live pace available. Using profile avg lap time as fallback: {stableAvgPace:F2}s"|Pace calculation fallback|Per lap|Yes|DOWNGRADE|[LalaLaunch:LAP]|"Pace fallback source=profile_avg lap_s={stableAvgPace:F2}"|
|LalaLaunch.cs|Pit/Pace baseline|Info|"[Pit/Pace] Baseline used = {stableAvgPace:F3}s (live median → profile avg → PB)."|Baseline selection|Per lap|Yes|KEEP|[LalaLaunch:PIT]|"Pace baseline source=live_median|profile|pb lap_s={stableAvgPace:F3}"|
|LalaLaunch.cs|Pace gross outlier|Info|"[Pace] Gross outlier lap {lastLapSec:F2}s (avg={paceBaselineForLog:F2}s, Δ={delta:F1}s)"|Lap rejected as outlier|Per lap|Yes|KEEP|[LalaLaunch:LAP]|"Pace reject type=gross_outlier lap_s={lastLapSec:F2} avg_s={paceBaselineForLog:F2} delta_s={delta:F1}"|
|LalaLaunch.cs|Pace too-slow|Info|"[Pace] Rejected too-slow lap {lastLapSec:F2}s (avg={paceBaselineForLog:F2}s, Δ={delta:F1}s)"|Lap rejected slow|Per lap|Yes|KEEP|[LalaLaunch:LAP]|"Pace reject type=too_slow lap_s={lastLapSec:F2} avg_s={paceBaselineForLog:F2} delta_s={delta:F1}"|
|LalaLaunch.cs|Various trace logs (prefix param)|Info|"{log}"|Ad-hoc debug strings|Depends|Maybe|REWRITE|[LalaLaunch:TRACE]|"Trace {key}=..." (structure per site)|
|LalaLaunch.cs|SaveRefuelRateToActiveProfile|Info|"[Profiles] RefuelRate saved for '{ActiveProfile.ProfileName}': {rateLps:F3} L/s"|Refuel rate persisted|User action|Yes|KEEP|[LalaLaunch:PROFILE]|"Profile refuel_rate_saved profile={ActiveProfile.ProfileName} rate_lps={rateLps:F3}"|
|LalaLaunch.cs|SaveRefuelRateToActiveProfile|Info|"[Profiles] SaveRefuelRateToActiveProfile failed: {ex.Message}"|Exception|Rare|Yes|REWRITE|[LalaLaunch:ERROR]|"Profile refuel_rate_save_failed profile={ActiveProfile?.ProfileName} err={ex.Message}"|
|LalaLaunch.cs|Apply profile|Info|"[Profiles] Applied profile to live and refreshed Fuel."|Profile applied|User action|Yes|KEEP|[LalaLaunch:PROFILE]|"Profile applied profile={ActiveProfile?.ProfileName}"|
|LalaLaunch.cs|SavePitTimeLoss|Warn|"LalaLaunch: Cannot save pit time loss – no active profile or track."|Attempt to save without context|User action|Yes|KEEP|[LalaLaunch:WARN]|"Pit save_loss skipped reason=no_profile_or_track"|
|LalaLaunch.cs|SavePitTimeLoss|Info|"[Pit/Pace] Saved PitLaneLoss = {rounded:0.00}s ({src})."|Pit lane loss saved|User action|Yes|KEEP|[LalaLaunch:PIT]|"Pit lane_loss_saved loss_s={rounded:0.00} source={src}"|
|LalaLaunch.cs|PitLite|Info|"[PitLite] Pit Lite Data used for DTL."|Using pit lite data|Per pit|Yes|DOWNGRADE|[LalaLaunch:PIT]|"Pit lite_dtl_used"|
|LalaLaunch.cs|Simplified probe|Warn|"[LalaLaunch] Simplified Car/Track probe failed: {ex.Message}"|Probe exception|Rare|Yes|KEEP|[LalaLaunch:WARN]|"State probe_failed err={ex.Message}"|
|LalaLaunch.cs|Session start snapshot|Info|"[LalaLaunch] Session start snapshot: Car='{CurrentCarModel}'  Track='{CurrentTrackName}'"|Session start|Per session|Yes|KEEP|[LalaLaunch:SESSION]|"Session snapshot_start car={CurrentCarModel} track={CurrentTrackName}"|
|LalaLaunch.cs|Refuel start|Info|"[LalaLaunch] Refuel started at {_refuelStartTime:F1}s (Fuel={_refuelStartFuel:F1})"|Refuel detection|Per pit|Yes|KEEP|[LalaLaunch:FUEL]|"Fuel refuel_start time_s={_refuelStartTime:F1} fuel_l={_refuelStartFuel:F1}"|
|LalaLaunch.cs|Refuel end|Info|"[LalaLaunch] Refuel ended at {stopTime:F1}s"|Refuel end|Per pit|Yes|KEEP|[LalaLaunch:FUEL]|"Fuel refuel_end time_s={stopTime:F1} added_l={fuelAdded:F1}"|
|LalaLaunch.cs|Launch abort|Info|"[LalaLaunch] Off track or in pits – aborting launch state to Idle."|Abort launch|Per event|Yes|KEEP|[LalaLaunch:STATE]|"Launch abort reason=offtrack_or_pit"|
|LalaLaunch.cs|PB candidate|Info|"[PB] candidate={lapMs}ms car='{CurrentCarModel}' trackKey='{CurrentTrackKey}' -> {(accepted ? "accepted" : "rejected")}"|Lap candidate evaluation|Per lap|Yes|REWRITE|[LalaLaunch:LAP]|"PB candidate lap_ms={lapMs} car={CurrentCarModel} track={CurrentTrackKey} result={accepted}"|
|LalaLaunch.cs|Auto-select profile|Info|"[LalaLaunch] New live combo detected. Auto-selecting profile for Car='{CurrentCarModel}', Track='{trackIdentity}'."|Live combo change|Per change|Yes|KEEP|[LalaLaunch:STATE]|"Profile auto_select car={CurrentCarModel} track={trackIdentity}"|
|LalaLaunch.cs|EnsureCarTrack hook|Info|"[LalaLaunch] EnsureCarTrack hook -> car='{CurrentCarModel}', trackKey='{CurrentTrackKey}'"|Ensure track with key|Per change|Yes|DOWNGRADE|[LalaLaunch:STATE]|"Profile ensure_track_hook car={CurrentCarModel} track_key={CurrentTrackKey}"|
|LalaLaunch.cs|EnsureCarTrack fallback|Info|"[LalaLaunch] EnsureCarTrack fallback -> car='{CurrentCarModel}', trackName='{trackIdentity}'"|Fallback ensure|Per change|Yes|DOWNGRADE|[LalaLaunch:STATE]|"Profile ensure_track_fallback car={CurrentCarModel} track_name={trackIdentity}"|
|LalaLaunch.cs|Session on-track reset|Info|"[LalaLaunch] New session on-track activity detected. Resetting all values."|On-track detected|Per session|Yes|KEEP|[LalaLaunch:SESSION]|"Session ontrack_reset"|
|LalaLaunch.cs|Auto mode|Info|"[LalaLaunch] OnTrack detected – mode=auto, page='{Screens.CurrentPage}'."|Auto mode entry|Per session|Yes|DOWNGRADE|[LalaLaunch:STATE]|"Session ontrack_auto page={Screens.CurrentPage}"|
|LalaLaunch.cs|Auto timer expired|Info|"[LalaLaunch] Auto mode timer expired – mode set to 'manual'."|Timer expiry|Per session|Yes|KEEP|[LalaLaunch:STATE]|"Session auto_timer_expired"|
|LalaLaunch.cs|Finish checkered leader|Info|"[LeaderFinish] Checkered flag detected – leader finished latched"|Leader finish detected|Per race|Yes|KEEP|[LalaLaunch:SESSION]|"Finish checkered source=leader latched=true"|
|LalaLaunch.cs|Fuel leader parse error|Info|"[FuelLeader] TryReadSeconds error for value '{raw}': {ex.Message}"|Parsing external fuel leader data|Rare|Yes|DOWNGRADE|[LalaLaunch:FUEL]|"Fuel leader_parse_failed value={raw} err={ex.Message}"|
|LalaLaunch.cs|Fuel leader missing|Info|"[FuelLeader] no valid leader lap time from any candidate – returning 0"|No leader data|Per event|Yes|KEEP|[LalaLaunch:FUEL]|"Fuel leader_missing result=0"|
|LalaLaunch.cs|Manual launch timeout|Info|"[LalaLaunch] Manual launch timed out after 30 seconds."|Manual launch expired|Rare|Yes|KEEP|[LalaLaunch:STATE]|"Launch manual_timeout seconds=30"|
|LalaLaunch.cs|CSV logging error|Error|"LaunchPlugin: CSV Logging Error: {ex.Message}"|Telemetry CSV write failure|Rare|Yes|KEEP|[LalaLaunch:ERROR]|"Trace csv_write_failed err={ex.Message}"|
|LalaLaunch.cs|LaunchTrace discarded|Info|"[LaunchTrace] Discarded trace file: {_currentFilePath}"|Discard trace|Per trace end|Yes|DOWNGRADE|[LalaLaunch:TRACE]|"Trace discard path={_currentFilePath}"|
|LalaLaunch.cs|Discard trace error|Error|"LaunchPlugin: Failed to discard trace file: {ex.Message}"|Deletion failure|Rare|Yes|KEEP|[LalaLaunch:ERROR]|"Trace discard_failed path={_currentFilePath} err={ex.Message}"|
|LalaLaunch.cs|New trace opened|Info|"[LaunchTrace] New launch trace file opened: {_currentFilePath}"|Start trace|Per session launch|Yes|KEEP|[LalaLaunch:TRACE]|"Trace opened path={_currentFilePath}"|
|LalaLaunch.cs|Start trace error|Error|"TelemetryTraceLogger: Failed to start new launch trace: {ex.Message}"|Trace open failure|Rare|Yes|KEEP|[LalaLaunch:ERROR]|"Trace open_failed err={ex.Message}"|
|LalaLaunch.cs|Write telemetry error|Error|"TelemetryTraceLogger: Failed to write telemetry data: {ex.Message}"|Trace write failure|Per failure|Yes|KEEP|[LalaLaunch:ERROR]|"Trace write_failed err={ex.Message}"|
|LalaLaunch.cs|Append summary warn|Warn|"TelemetryTraceLogger: Cannot append summary. Trace file path is invalid or file does not exist."|Append summary without file|Rare|Yes|KEEP|[LalaLaunch:WARN]|"Trace append_summary_skipped reason=missing_file path={_currentFilePath}"|
|LalaLaunch.cs|Summary appended|Info|"[LaunchTrace] Successfully appended launch summary to {_currentFilePath}"|Summary append success|Per launch|Yes|KEEP|[LalaLaunch:TRACE]|"Trace append_summary_success path={_currentFilePath}"|
|LalaLaunch.cs|Append summary error|Error|"TelemetryTraceLogger: Failed to append launch summary using File.AppendAllLines: {ex.Message}"|Append failure|Rare|Yes|KEEP|[LalaLaunch:ERROR]|"Trace append_summary_failed path={_currentFilePath} err={ex.Message}"|
|LalaLaunch.cs|Trace closed|Info|"[LaunchTrace] Launch trace file closed: {_currentFilePath}"|Close trace|Per launch|Yes|KEEP|[LalaLaunch:TRACE]|"Trace closed path={_currentFilePath}"|
|LalaLaunch.cs|Stop trace error|Error|"TelemetryTraceLogger: Error stopping launch trace: {ex.Message}"|Stop failure|Rare|Yes|KEEP|[LalaLaunch:ERROR]|"Trace stop_failed path={_currentFilePath} err={ex.Message}"|
|LalaLaunch.cs|Trace dir missing|Info|"[LaunchTrace] Trace directory not found: {tracePath}"|Listing traces with missing dir|Rare|Yes|DOWNGRADE|[LalaLaunch:TRACE]|"Trace list_missing_dir path={tracePath}"|
|LalaLaunch.cs|Trace list error|Error|"TelemetryTraceLogger: Error getting launch trace files: {ex.Message}"|Enumerate failure|Rare|Yes|KEEP|[LalaLaunch:ERROR]|"Trace list_failed err={ex.Message}"|
|LalaLaunch.cs|Trace file missing|Warn|"TelemetryTraceLogger: Trace file not found: {filePath}"|File missing on read|Rare|Yes|KEEP|[LalaLaunch:WARN]|"Trace read_skipped reason=missing_file path={filePath}"|
|LalaLaunch.cs|Trace read error|Error|"TelemetryTraceLogger: Error reading launch trace file '{filePath}': {ex.Message}"|Read failure|Rare|Yes|KEEP|[LalaLaunch:ERROR]|"Trace read_failed path={filePath} err={ex.Message}"|
|LalaLaunch.cs|Parse row error|Error|"TelemetryTraceLogger: Error parsing telemetry data row: {ex.Message}. Line:'{line}'"|Parse telemetry row|Per failure|Yes|KEEP|[LalaLaunch:ERROR]|"Trace parse_failed err={ex.Message} line={line}"|
|LalaLaunch.cs|Parse row failed|Error|"TelemetryTraceLogger: Failed parsing row: {ex.Message} | Line: {line}"|Parse failure alt path|Per failure|Yes|KEEP|[LalaLaunch:ERROR]|"Trace parse_failed_alt err={ex.Message} line={line}"|
|PitCycleLite.cs|Arm entry|Info|"[PitLite] ENTRY edge detected – arming cycle and clearing previous pit figures."|Pit entry edge|Per pit|Yes|KEEP|[LalaLaunch:PIT]|"PitLite edge=entry action=arm"|
|PitCycleLite.cs|Exit edge|Info|"[PitLite] EXIT edge detected – latching lane/box timers from PitEngine."|Pit exit edge|Per pit|Yes|KEEP|[LalaLaunch:PIT]|"PitLite edge=exit action=latched"|
|PitCycleLite.cs|Latch timers|Info|"[PitLite] Latched In-lap = {InLapSec:F2}s."|In-lap latched|Per pit|Yes|KEEP|[LalaLaunch:PIT]|"PitLite inlap_latched sec={InLapSec:F2}"|
|PitEngine.cs|Invalid direct travel|Warn|"PitEngine: Ignoring invalid Direct Travel Time ({direct:F2}s)"|Direct time invalid|Per pit calc|Yes|KEEP|[LalaLaunch:PIT]|"PitEngine direct_travel_invalid sec={direct:F2}"|
|PitEngine.cs|Pit lap latched|Info|"[PitEngine] Pit-lap invalid – aborting pit-cycle evaluation."|Pit lap invalid|Per pit|Yes|KEEP|[LalaLaunch:PIT]|"PitEngine pit_lap_invalid reason=criteria"|
|PitEngine.cs|Pit lap captured|Info|"[PitEngine] Pit-lap captured = {_pitLapSeconds:F2}s – awaiting out-lap completion."|Pit lap complete|Per pit|Yes|KEEP|[LalaLaunch:PIT]|"PitEngine pit_lap_captured sec={_pitLapSeconds:F2}"|
|PitEngine.cs|Out-lap invalid|Info|"[PitEngine] Out-lap invalid – aborting pit-cycle evaluation."|Out lap invalid|Per pit|Yes|KEEP|[LalaLaunch:PIT]|"PitEngine outlap_invalid"|
|FuelCalcs.cs|Strategy reset|Info|"[FuelCalcs] Strategy reset – defaults applied."|Strategy reset|User action|Yes|KEEP|[LalaLaunch:FUEL]|"Fuel strategy_reset source=user"|
|FuelCalcs.cs|Init presets|Error|"FuelCalcs.InitPresets: " + ex.Message|Preset init exception|Startup|Yes|KEEP|[LalaLaunch:ERROR]|"Fuel presets_init_failed err={ex.Message}"|
|FuelCalcs.cs|Leader snapshot reset|Info|"[Leader] ResetSnapshotDisplays: cleared live snapshot including leader delta."|Reset snapshot|Per reset|Yes|DOWNGRADE|[LalaLaunch:TRACE]|"Leader snapshot_reset"|
|FuelCalcs.cs|Fuel summary string.Format|Info|string.Format("[Fuel] Summary ...")|Fuel summary periodic|Per lap/segment|Yes|KEEP|[LalaLaunch:FUEL]|"Fuel summary ..." structured|
|CarProfiles.cs|Various ensures|Info|(several EnsureTrack logs)|Profile track ensure|Per call|Yes|DOWNGRADE|[LalaLaunch:PROFILE]|"Profile track ensure ..."|
|RejoinAssistEngine.cs|MsgCx override|Info|"[RejoinAssist] MsgCx override triggered."|Message override|Rare|Yes|KEEP|[LalaLaunch:STATE]|"RejoinAssist msg_override"|
|LaunchAnalysisControl.xaml.cs|Delete trace|Info|"[LaunchTrace] Deleted trace file: {fullPath}"|User deletes trace|User action|Yes|KEEP|[LalaLaunch:TRACE]|"Trace delete path={fullPath}"|
|LaunchAnalysisControl.xaml.cs|Delete file error|Error|"LaunchPlugin: Error deleting file: {ex.Message}"|Delete failure|User action|Yes|KEEP|[LalaLaunch:ERROR]|"Trace delete_failed path={fullPath} err={ex.Message}"|
|LaunchAnalysisControl.xaml.cs|Directory missing|Warn|"LaunchAnalysis: Directory not found: {pathToScan}"|Missing directory during scan|User action|Yes|KEEP|[LalaLaunch:WARN]|"Trace list_skipped reason=missing_directory path={pathToScan}"|
|LaunchAnalysisControl.xaml.cs|File missing|Warn|"LaunchAnalysis: Selected file not found at {fullPath}"|Selected file missing|User action|Yes|KEEP|[LalaLaunch:WARN]|"Trace open_failed reason=missing_file path={fullPath}"|
|PitEngine.cs|Pit events (additional)|Info|"[PitEngine] ..." captures/logs|Pit cycle states|Per pit|Yes|REWRITE|[LalaLaunch:PIT]|Structured state logs|

## Part C — Lap-Crossing Narrative Set

- **Pace summary**
  - Trigger: Lap acceptance path where pace/fuel summary is currently logged (`LogLapSummary` around LapDetector pipeline in `LalaLaunch.cs`).
  - Template: `[LalaLaunch:LAP] Lap result={accepted|rejected} reason={reason} lap_s={lapSec:F3} baseline_s={baseline:F3} delta_s={delta:F3}`.
  - Variables: acceptance flag, rejection reason (outlier/slow/low-speed), lap time, baseline, delta.

- **Fuel summary**
  - Trigger: Same lap acceptance block when fuel model updates and `[LiveFuel]` seeds processed.
  - Template: `[LalaLaunch:FUEL] Fuel decision result={accepted|rejected} mode={dry|wet} confidence={confidence:P0} lap_fuel={fuelPerLap:F2}`.
  - Variables: fuel acceptance, dry/wet mode, confidence %, per-lap fuel used.

- **Pit-cycle completion summary**
  - Trigger: Pit cycle completion in `PitEngine` when pit/out-lap latched and PitLaneLoss saved.
  - Template: `[LalaLaunch:PIT] Pit cycle complete lane_loss_s={pitLaneLoss:F2} source={src} inlap_s={InLapSec:F2} outlap_s={OutLapSec:F2}`.
  - Variables: lane loss, source (manual/lite/derived), in-lap/out-lap durations.

- **Finish/checkered detection summary**
  - Trigger: `LeaderFinish` detection block in `LalaLaunch.cs` where checkered is latched.
  - Template: `[LalaLaunch:SESSION] Finish detected trigger={leader|class_leader|player} lap={CompletedLaps} time_s={SessionTime}`.
  - Variables: trigger entity, player laps, session time.

## Part D — Missing Production-Worthy Logs

- Add INFO log when lap is rejected/accepted with consolidated reason (covers pace + fuel) at lap boundary; avoids per-tick spam.
- Add INFO log when fuel mode switches dry↔wet with source of decision and confidence.
- Add INFO log when pit-entry/exit detected from telemetry with captured timestamps and state reset outcome.
- Add INFO log when session type changes (practice/qual/race) with car/track snapshot to correlate settings.
- Add DEBUG logs around planner maths (pace baseline selection, fuel contingency calculations) gated by verbose flag; avoid per-tick spam.
- Add INFO log when CSV/trace logging toggled on/off by user to correlate file creation/deletion flows.
