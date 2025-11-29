# Fuel tab profile refresh behavior

**Context**: Switching car profiles while the fuel planner is in *Profile* mode previously left some UI fields (fuel burn, lap times, helper text) showing data from the prior car until the user toggled tracks.

**Findings**
- The track dropdown uses `TrackStats` instances that are rebuilt when a new `CarProfile` is selected.
- When the user chose the same circuit on two different cars, the selected track reference stayed the same, so `LoadProfileData()` was not invoked and profile-derived fields (fuel burn, lap time, helper text) stayed on the old car.

**Fix**
- Preserve the chosen circuit when repopulating the track list and, if the selection reference is unchanged after a car swap, force `LoadProfileData()` to refresh the profile-derived fields for the new car (`SelectedCarProfile` setter in `FuelCalcs.cs`).
