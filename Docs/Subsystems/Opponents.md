# Opponents subsystem

Purpose: own all opponent-facing calculations for nearby pace/fight prediction and pit-exit position forecasting with SimHub exports under `Opp.*` and `PitExit.*`.

## Gating and scope
- Runs **race sessions only**; resets and publishes defaults outside Race.【F:Opponents.cs†L42-L88】
- Additional lap gate: requires **CompletedLaps ≥ 2** before any Opp/PitExit outputs become valid. Gate opening logs once per activation.【F:Opponents.cs†L58-L88】
- Uses **IRacingExtraProperties only**; no Dahl DLL or SDK arrays.

## Identity model
- Stable key: `ClassColor:CarNumber`. Empty class+number returns blank identity (slot ignored).【F:Opponents.cs†L90-L100】
- Nearby slot changes log once per rebind when active; identity caches persist pace history across slot swaps.【F:Opponents.cs†L197-L305】

## Data inputs
- Nearby targets: `iRacing_DriverAheadInClass_00/01_*`, `iRacing_DriverBehindInClass_00/01_*` for Name, CarNumber, ClassColor, RelativeGapToPlayer, LastLapTime, BestLapTime, IsInPit, IsConnected.【F:Opponents.cs†L197-L305】
- Leaderboard scan (00–63 until empty row): `iRacing_ClassLeaderboard_Driver_XX_*` for Name, CarNumber, ClassColor, PositionInClass, RelativeGapToLeader, IsInPit/IsConnected, LastLapTime, BestLapTime.【F:Opponents.cs†L352-L387】
- Player identity: `iRacing_Player_ClassColor`, `iRacing_Player_CarNumber`. Pit-exit uses pit loss passed in from LalaLaunch (FuelCalculator.PitLaneTimeLoss).【F:Opponents.cs†L42-L88】【F:LalaLaunch.cs†L3622-L3633】

## Pace cache & blended pace
- Entity cache keyed by identity; keeps best lap and a 5-lap ring buffer of valid recent laps (rejects ≤0/NaN/huge, skips laps flagged in-pit).【F:Opponents.cs†L551-L688】
- BlendedPaceSec = 0.70×RecentAvg + 0.30×(BestLap×1.01); falls back to recent-only or best×1.01 if missing.【F:Opponents.cs†L652-L687】

## Fight prediction (dash support)
- Uses my pace (from LalaLaunch) vs opponent blended pace once gate active.【F:Opponents.cs†L42-L88】【F:LalaLaunch.cs†L3622-L3633】
- Ahead: closingRate = opponent − mine; if ≥ −0.05 → “no catch”; else LapsToFight = gap / −closingRate.【F:Opponents.cs†L213-L262】
- Behind: closingRate = mine − opponent; if ≥ −0.05 → “no threat”; else LapsToFight = gap / −closingRate.【F:Opponents.cs†L213-L262】
- Summary string concatenates A1/A2/B1/B2 compact status for dashboards.【F:Opponents.cs†L102-L135】

## Pit-exit prediction
- Finds player row in class leaderboard; gapToPlayer = opp.RelGapToLeader − player.RelGapToLeader. predictedGapAfterPit = gapToPlayer − pitLossSec.【F:Opponents.cs†L477-L536】
- PredictedPosition = 1 + count of same-class connected cars where predictedGapAfterPit < 0. Logs when validity toggles or predicted position changes while active.【F:Opponents.cs†L525-L548】
- Publishes PitExit.Valid/PredictedPositionInClass/CarsAheadAfterPitCount/Summary; defaults reset when invalid.【F:Opponents.cs†L456-L548】【F:Opponents.cs†L745-L758】

## Outputs
- `Opp.Ahead1/2.*`, `Opp.Behind1/2.*` → Name, CarNumber, ClassColor, GapToPlayerSec, BlendedPaceSec, PaceDeltaSecPerLap, LapsToFight (NaN = no fight/invalid). Summary at `Opp.Summary`.【F:Opponents.cs†L205-L262】【F:Opponents.cs†L690-L743】
- Optional leader pace: `Opp.Leader.BlendedPaceSec`, `Opp.P2.BlendedPaceSec`.【F:Opponents.cs†L84-L88】【F:Opponents.cs†L690-L720】
- Pit exit exports: `PitExit.Valid`, `PitExit.PredictedPositionInClass`, `PitExit.CarsAheadAfterPitCount`, `PitExit.Summary`.【F:Opponents.cs†L477-L548】【F:Opponents.cs†L745-L758】
