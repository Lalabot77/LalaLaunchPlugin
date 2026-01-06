# Opponents subsystem

Purpose: own all opponent-facing calculations for nearby pace/fight prediction and pit-exit position forecasting with SimHub exports under `Opp.*` and `PitExit.*`.

## Gating and scope
- Runs **race sessions only**; resets caches/outputs if session leaves Race.【F:Opponents.cs†L42-L88】
- Additional lap gate: requires **CompletedLaps ≥ 2** before any Opp/PitExit outputs become valid. Data is still ingested pre-gate, but outputs/logging are gated. Gate opening logs once per activation.【F:Opponents.cs†L58-L88】
- Uses **IRacingExtraProperties only**; no Dahl DLL or SDK arrays.【F:Opponents.cs†L42-L88】

## Identity model
- Stable key: `ClassColor:CarNumber`. Empty class+number returns blank identity (slot ignored).【F:Opponents.cs†L90-L100】
- Nearby slot changes log once per rebind when active; identity caches persist pace history across slot swaps. Logging follows the lap gate (no chatter before lap ≥2).【F:Opponents.cs†L252-L361】

## Data inputs
- Nearby targets: `iRacing_DriverAheadInClass_00/01_*`, `iRacing_DriverBehindInClass_00/01_*` for Name, CarNumber, ClassColor, RelativeGapToPlayer, LastLapTime, BestLapTime, IsInPit, IsConnected.【F:Opponents.cs†L252-L344】
- Leaderboard scan (00–63 until empty row): `iRacing_ClassLeaderboard_Driver_XX_*` for Name, CarNumber, ClassColor, PositionInClass, RelativeGapToLeader, IsInPit/IsConnected, LastLapTime, BestLapTime.【F:Opponents.cs†L395-L430】
- Player identity: `iRacing_Player_ClassColor`, `iRacing_Player_CarNumber`. Pit-exit receives pit loss from LalaLaunch’s stop-loss calculation (validated to ≥0).【F:Opponents.cs†L42-L88】【F:LalaLaunch.cs†L4144-L4181】

## Pace cache & blended pace
- Entity cache keyed by identity; keeps best lap and a 5-lap ring buffer of valid recent laps (rejects ≤0/NaN/huge, skips laps flagged in-pit).【F:Opponents.cs†L627-L717】
- BlendedPaceSec = 0.70×RecentAvg + 0.30×(BestLap×1.01); falls back to recent-only or best×1.01 if missing.【F:Opponents.cs†L693-L717】

## Fight prediction (dash support)
- Uses my pace (from LalaLaunch) vs opponent blended pace once gate active; my pace is sanitized to remove invalid/huge values.【F:Opponents.cs†L42-L88】【F:Opponents.cs†L82-L88】
- Gap is stored as the absolute of the relative gap input for display consistency.【F:Opponents.cs†L268-L317】
- Ahead: closingRate = opponent − mine; requires closingRate > +0.05 s/lap and positive gap to publish LapsToFight (capped at 999). Otherwise NaN (no catch).【F:Opponents.cs†L268-L317】
- Behind: closingRate = mine − opponent; requires closingRate > +0.05 s/lap and positive gap to publish LapsToFight (NaN when no threat/invalid).【F:Opponents.cs†L268-L317】
- Summary string concatenates A1/A2/B1/B2 compact status for dashboards.【F:Opponents.cs†L102-L135】

## Pit-exit prediction
- Finds player row in class leaderboard; gapToPlayer = opp.RelGapToLeader − player.RelGapToLeader. predictedGapAfterPit = gapToPlayer − pitLossSec (pit loss forced to 0 when invalid).【F:Opponents.cs†L507-L553】
- PredictedPosition = 1 + count of same-class connected cars where predictedGapAfterPit < 0. Logs when validity toggles or predicted position changes while active.【F:Opponents.cs†L532-L566】
- Publishes PitExit.Valid/PredictedPositionInClass/CarsAheadAfterPitCount/Summary; defaults reset when invalid.【F:Opponents.cs†L507-L579】【F:Opponents.cs†L775-L789】

## Outputs
- `Opp.Ahead1/2.*`, `Opp.Behind1/2.*` → Name, CarNumber, ClassColor, GapToPlayerSec (absolute), BlendedPaceSec, PaceDeltaSecPerLap, LapsToFight (NaN = no fight/invalid). Summary at `Opp.Summary`.【F:Opponents.cs†L252-L343】【F:Opponents.cs†L268-L317】【F:Opponents.cs†L720-L750】
- Optional leader pace: `Opp.Leader.BlendedPaceSec`, `Opp.P2.BlendedPaceSec`.【F:Opponents.cs†L84-L88】【F:Opponents.cs†L720-L736】
- Pit exit exports: `PitExit.Valid`, `PitExit.PredictedPositionInClass`, `PitExit.CarsAheadAfterPitCount`, `PitExit.Summary`.【F:Opponents.cs†L507-L566】【F:Opponents.cs†L775-L789】
