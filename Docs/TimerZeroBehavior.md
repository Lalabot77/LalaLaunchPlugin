# iRacing timer-zero behavior (time-limited races)

This note summarizes common iRacing behavior for time-limited races and how it should shape the "extra seconds after timer zero" estimate used by the fuel calculator.

## What happens when the race clock hits zero

* **Time + one lap rule:** Once the session timer expires, the leader receives the white flag the next time they cross the start/finish line. They must still complete that white-flag lap before the checkered flies. This matches iRacing’s public guidance and community documentation about timed events.
* **Minimum and maximum extra distance:** Because the white flag is thrown on the leader's *first* crossing after zero, the race ends after roughly one to almost two additional laps of leader running, depending on where the timer expired within the lap. The calculator should therefore anchor the post-zero clock to the leader’s next crossing, not simply to your next crossing.
* **Field dismissal:** When the leader takes the checkered at the end of the white-flag lap, other cars receive the checkered the next time they reach start/finish. Drivers who are behind the leader on track typically only need to finish the lap they are on when the leader takes the checkered; drivers ahead of the leader on track will still need to finish the lap they are on after the leader finishes.

## Implications for the "extra time after zero" calculator

1) **Leader white-flag timing**
   * Compute `leaderPhase = raceSeconds % leaderLapSec` and set `leaderTimeToLine = leaderPhase <= 1e-6 ? leaderLapSec : leaderLapSec - leaderPhase`.
   * White flag happens at `leaderTimeToLine` seconds after zero (the leader’s first crossing after expiry).
   * Leader's checkered happens at the same crossing when they finish that lap: `leaderTimeToLine`.

2) **Your drive window after zero**
   * If you are **behind** the leader on track (most common), your race ends on the first crossing you make *after* the leader finishes their final lap. If your own remainder-to-line is greater than or equal to the leader's finish window, you finish this lap; otherwise you may need one or more full laps until your next crossing occurs after the leader has taken the flag.
   * If you are **ahead** of the leader on track when the clock hits zero, you will take the white on your *next* crossing and still need to finish that white-flag lap, making your post-zero time roughly the remainder of your current lap plus one or more laps. This is the expensive edge case the calculator must guard against.

3) **Effect of pace sliders**
   * Slower **leader** pace (smaller negative delta) delays the white flag and checkered relative to the clock, increasing how much of your own lap you can finish before the leader ends the race.
   * Slower **your pace** lengthens both your remainder to the line and any laps you owe after the white flag, increasing the extra-seconds estimate.

4) **Recommended modeling tweaks**
   * Anchor the baseline extra time to the leader’s first crossing after expiry (`leaderTimeToLine`), then advance your own crossings until you reach one that occurs after that leader crossing.
   * When you are ahead on track (leader will not reach you before you next cross), add as many full laps as required after your next crossing to occur after the leader’s finish.

These rules should keep single-stop races stable while improving the edge cases for multi-stop timed events where the clock expiry is close to the start/finish line.
