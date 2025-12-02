# LALA-036: Extra time after zero sanity check

This quick check demonstrates that the new continuous `ComputeExtraSecondsAfterTimerZero` model now varies smoothly with the "Your Pace vs Leader (s)" slider.

Test parameters:
- Race length: 60 minutes (3,600 seconds)
- Your lap: 1:46.555 (106.555 seconds)
- Pace vs leader swept from 0.0 to 5.0 seconds (positive means the leader is that many seconds faster).

Computed outputs (seconds after timer zero):

| Pace vs Leader (s) | Est. Drive Time After Zero (s) |
| --- | --- |
| 0.0 | 106.56 |
| 1.0 | 140.66 |
| 2.0 | 175.42 |
| 3.0 | 210.85 |
| 4.0 | 246.97 |
| 5.0 | 283.80 |

The values rise steadily with every 1s slider increment, with no plateaus or stepwise jumps, matching the smooth fuel calculation behaviour.
