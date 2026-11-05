# Contract revision history

Moved verbatim out of `CLAUDE.md` on 2026-11-04, where it had grown to most of the
file. `CLAUDE.md` keeps the current state; this is the record of how it got there:
every contract revision from 10 to 17, every run trained against them, and why each
promoted brain was refused. Commit hashes named here are the only way to promote or
realism-check a run from an older revision.

Note (2026-11-04): every `results/` folder named below is gone, and so is `.venv`.

---

**`Assets/Agents/Football_v02` is promoted but REFUSED, so every player runs
`Heuristic` until a revision 17 run (`football_base25`) is promoted.** v02 came from
`football_base14` (contract revision 12, 2026-10-03), a run cut short to 1.2M steps
with a quarterback only 32k steps old and a defense that never learned to pursue
(`results/football_base14/training_report.html`). It FAILED the realism gate at 6.80
yards per play (see its MANIFEST.md), and revision 13 changed what a throw and a
tackle do, which the table's revision 12 stamp now correctly refuses.

**`football_base15` (revision 13, 12.7M / 1.27M steps) FINISHED ON 2026-10-04 AND WAS
NEVER PROMOTED.** Revision 14 changed the shapes as well as the game, so it cannot
load in this build at all. To promote or realism-check it, check out `981ba23`, the
last revision 13 commit — and it will still only ever run in a revision 13 build.

`Assets/Agents/Football_v01` is still on disk and is older still: `football_base12`,
contract revision 10, 7M steps. While the build was on revision 10 all 22 players
ran it — verified in a played game, 22 of 22 agents with a model. It FAILS the
realism gate (18.8 yards per play, 0.78 TDs per drive, 97% completions; see its
MANIFEST.md) because the offense found two rules defects, and fixing those rules was
revision 11.

An EARLIER `Assets/Agents/Football_v01` and its table existed until 2026-08-22 and
were deleted, for the reason the stamp exists. They came from `football_base08` at
**contract revision 4** against a build now on **revision 7**. The table was worse
than merely old: it carried six entries numbered 0..5 from the six-brain-group era,
and `Systems_BrainGroup` has had three members since revision 7, so entries 3-5 were
undefined enum values and 0-2 pointed at entirely different populations than their
names suggested. Only the revision mismatch stood between it and a silently
scrambled load — and re-stamping the revision by hand, which is exactly what someone
does when they want the brains to load, would have removed it.

So `Agent_BrainTable.MatchesCurrentContract` now checks the **group set** as well as
the revision stamp, because the stamp is one integer and cannot see the entries
under it.

To promote: train against a config for the CURRENT contract revision, run
`Tools/promote_brain.py`, then **`Tools > PoFootball > Build Brain Table`** in the
Editor. That last step is not optional — the Python side copies `.onnx` files but
cannot write the ScriptableObject that lists them.

**THE CURRENT REVISION IS 17, AND `Config/FootballBase25.yaml` IS ITS CONFIG
(written 2026-10-22, NOT YET RUN).** Every shape is identical to revision 16; what
moved is what the two continuous actions MEAN. They were a throttle along the facing
and a steering torque, so a policy had to learn to turn a rotating body before it
could go anywhere, and the defense never did (base22 at 13-15% of top speed; base24
at 5 h: no sack, a touchdown on 28% of plays). They are now a VELOCITY — forward and
leftward in the body's frame at the decision, at |command| × top speed — and
`Agent_VelocityController` turns, tracks the speed, BRAKES when asked for less (a
throttle could only coast: 11 m from a receiver's top speed) and backpedals facing
forward up to `BACKPEDAL_SPEED_FRACTION` (0.6; negative drive used to backpedal at
full sprint). Forces, damping and fatigue are unchanged. The scripted players drive
the same controller — `Steer` asks for a direction now — so **the scripted game moved
too and its realism must be re-measured on the four fixed seeds**. Also revision 17:
the quarterback's release with no pass on pitches to the nearest trailing man when
nobody is along the aim (`Agent_ActionLegality.TryPitch`); the mask enabled that
release and the aimed pitch then refused it, found by `Agent_ActionLegalityTests`.
The mask and the handlers now read one set of pure predicates (`Agent_ActionLegality`).
Slot 0 stays forward and slot 1 is leftward because that is how drive and positive
steer pointed, so `FootballBase25` warm-starts Offense, Quarterback and DefenseCall
from `football_base24` by `init_path`; the Defense brain starts FRESH, because
base24's was still passive (no sack, touchdowns on 39% of plays at 5.5 h) and was
never promoted. **`football_base24` is a revision 16 run: promote or realism-check it
only from commit `5e53d25`.** FootballBase25 opens with a RUN-FIT DRILL
(`spot_lesson` 3, `Systems_RandomSpotProvider.LESSON_RUN_FIT`: downs 1-3, every pass
masked and refused) and graduates to FullGame when the DefenseCall brain — paid
exactly the defense's terminal reward — averages 0.25 over 1000+ plays. Its header
has the numbers. Rebuild `Builds/FootballEnv` before it: a stale revision 16 env is
not refused by the handshake.

Revision 16's own history follows. **Revision 16's config was
`Config/FootballBase23.yaml`/`FootballBase24.yaml`.**
(`FootballBase18`, archived, was its first: `football_base18` kept the front spread
but its flag rate climbed from 4% to 12% of plays, because training priced a flag
at a flat 0.15 while a completion pays 0.45. `Systems_FoulPrice` now charges each
flag what it would give away in a game; a reward change, so still revision 16.
`FootballBase19`, archived, was that config — and its flags climbed back to 12% by
1.3M steps anyway, because the price was not the cause: replaying its checkpoints
in the Editor, 35 of 37 flags were ILLEGAL BLOCK IN THE BACK. The officials judged
"from behind" by the victim's VELOCITY, so a corner backpedalling in coverage —
moving away from a receiver while facing him — was hit in the back by every stalk
block; each new collision of the pair rolled again; and close-line play, where it
is legal (NFL Rule 12-1-3), was flagged. `Systems_PenaltySystem.TryBlockInTheBack`
now reads `Systems_IPlayerHandle.Facing`, judges a pair once a play, and exempts
the close-line zone: the same checkpoints went from 8-12% of plays flagged to
1.6%. Only reward moves in training, so still revision 16, and `football_base20`
starts from `football_base19`'s weights with `--initialize-from`, at the lesson
base19 had reached. Its plays that hit the 600-tick ceiling then climbed to 6.8%,
and replaying its checkpoints showed two more defects. A stall was priced as a
tackle WITHOUT the first-down bonus, so an offense could not stall past the
sticks to collect it — but the reward is zero-sum, so the defense was paid for
exactly that: defenders shadowed carriers past the line to gain, 13-29 m off,
and never tackled. `Reward_Terminal` prices `TimeExpired` exactly as a tackle on
the same spot now, the only price neither side can prefer. And a kickoff could be
PITCHED before it was kicked: the automatic kick waits for the kicker alone, so a
lateral to a lineman turned the kickoff into a kicking-team run that kept the
ball — 20-25 of 44-56 expired plays in a full game, none of them ever kicked.
`Systems_BallSystem.IsKickoffNotYetKicked` refuses it in the rule and the mask.
`football_base21` (`FootballBase21`) continues from base20's weights the same
way. Its ceiling plays came back to ~6% once FullGame began, and none was a stall:
every one a carrier 40-62 m past the line, running at 4-6 m/s with nobody within
17-54 m — a breakaway the 600-tick ceiling (which a PLAYED GAME has too) whistled
dead in midfield and priced as a tackle, so a breakaway was not worth chasing. At
the ceiling the official now projects the run (`Systems_Referee.IsGoneForTheGoal`):
if no opponent, at his top speed, reaches the carrier's path before the carrier at
the pace he is actually running, it is a touchdown. `football_base22`
(`FootballBase22`, FullGame only) continues from base21's weights. Steps restart at
zero on each `--initialize-from`; add 1.63M (base19), 1.58M (base20) and 1.40M
(base21) Offense steps to read a total.

`football_base22` (3.13M more) was promoted as `Assets/Agents/Football_v03` — on
disk, NOT committed, and NOT in the brain table — and FAILED the realism gate on
all five bands: 8.21 yards a play, 0.42 TDs a drive, 8.89 a carry, 0.87
completions, 0.10 first downs a play (`results/realism/realism-20261017-112731.json`).
Replayed, the cause was one PASSIVE Defense brain: with a carrier 10+ yards
downfield the defenders' mean drive output was 0.10, they faced him no more than
away from him and moved at 13-15% of top speed; at the line ~1 m/s, so no sacks in
three million steps and 89% of throws complete. It had been fitted for ~5M steps
while standing off paid. `football_base23` (`FootballBase23`) therefore trains the
DEFENSE BRAIN FROM SCRATCH while Offense, Quarterback and DefenseCall keep base22's
weights, through per-behavior `init_path` in the config — no `--initialize-from`.
`football_base24` (`FootballBase24`, launched 2026-10-22 16:18 for ten hours) continues all
four brains from base23's weights with one reward change, so still revision 16: a
defender is paid the fall in its TIME TO INTERCEPT the ball, not the distance
closed — closing distance is minimised by running at where the carrier is, which
at equal top speeds catches nobody. Potential-based shaping
(`Reward_Role.InterceptShaping`, Φ = 0.012·(8 − min(t, 8)), γ = 0.99 over 5 ticks):
policy-invariant, bounded at 0.096 per defender per play, nothing to a defender
out of reach. Its `init_path`s name `results/football_base23/*/checkpoint.pt`. The
whole `results/` folder (base12-23, 3.6 GB) had been sent to the Windows Recycle
Bin; on 2026-10-22 only `football_base23` was COPIED back (158 MB), so every other
run's results, `results/realism/` and the launcher scripts are still in the bin.
Rebuild the env first.)
Revision 16 (2026-10-16) gives the defense's front A BRAIN OF ITS OWN. Revision 15
read it off a second branch of the Defense brain, sampled from the middle
linebacker alone, and `football_base17` called the zero blitz on 37% of plays at
1M steps, 87% at 1.3M and 100% by 2M — one decision a play riding on a brain whose
every other sample is steering, at steering's entropy bonus, the trap the
quarterback's call fell into while it shared `OffenseSkill`. The front is now a
fourth behavior, **`DefenseCall`** (`Agent_DefensiveCoordinator`): no body, 12
observations (`Sensor_DefenseCallState`: down, distance, yards to goal, a
two-point flag, the offense's formation), no continuous actions, one branch `[9]`,
one decision a play once the quarterback's kick window has closed, paid the play's
defensive terminal reward, with its own exploration bonus (5e-2) in the config. The
middle linebacker's player creates it at runtime, so no scene carries it. The
Defense brain is back to `[2]`, the pitch. `football_base17` was stopped at ~2M
steps and is not promotable.

Revision 15 (2026-10-16, `Config/archive/FootballBase17.yaml`) is the merge of two revision 14s:
Both were built from
revision 13 in parallel — one on another machine on 2026-10-04, pushed to origin,
one here on 2026-10-15, never pulled against it — and they shared a number and
nothing else. Revision 15 keeps every shape of both: **50 observations; Quarterback
branches `[8, 2]`; Offense `[2]`, the pitch; Defense `[2, 9]`, the pitch and then the
front.** Where the two built the same thing, one was kept: the pre-snap is this
machine's (bodies set for `PRESNAP_TICKS`, the referee snaps), with the front
called inside it and lined up the tick it is called; pre-snap fouls are this
machine's refereed flinches, weighted by the other's discipline trait
(`Systems_SeededDisciplineModel`), and the other's between-plays roll is gone;
kicks are played, so `Systems_IKickModel` stays deleted; the pace is the other's
(120 s quarters, 2.5 s huddle, 1.2 s dead ball, no instant replay), with this
machine's clock rules scaled to it. **No brain of either revision 14 loads.**
`football_base16` (below) can be promoted only from commit `1417808`.

Revision 14, as written HERE (2026-10-15), PLAYS THE WHOLE GAME, and unlike 11-13
it changes the SHAPES: 45 observations (was 36) and a pitch branch on the Offense
and Defense brains.
`Agent_ActionContract` and `docs/GAME_LAYER.md` ("Rules implemented") have the detail:

- **Kicks are snaps.** Kickoffs, punts, field goals and the try after a touchdown
  line up in special-teams formations (`Systems_FormationBook`) and the ball is
  kicked, fielded, fair-caught, muffed, blocked, returned or downed
  (`Systems_BallSystem.Kick`, distances from `Systems_ISkillModel.DrawKick`). A
  fourth-down kick is called by the quarterback BEFORE the snap; every other call
  still latches at the dropback. `Systems_IKickModel` is gone.
- **The ball changes hands mid-play.** An interception or a recovered fumble is a
  return toward −Y; a stripped ball is loose and either side may fall on it; a
  carrier may pitch to a man level or behind. `Systems_BallModel.PossessingSide`
  and `Systems_PlayModel.ReturnOutcome` carry it.
- **A pile is down by forward progress**; a carrier standing still with nobody near
  has given himself up (`Downed`). `TimeExpired` is the trainer's ceiling only.
  **Since 2026-10-16 a carrier STOPPED IN A CROWD is down too** —
  `Systems_Referee.TrackCrowd`: an opponent within 3 m and under 0.3 m gained in
  `STALL_TICKS`, spotted at his furthest point. Forward progress needed a
  defender's CONTACT and the stall read the carrier's jostling SPEED, so a scrum
  shoved backwards through the carrier's own blockers was whistled by nothing but
  the 600-tick ceiling: 14% of training plays by 450k steps, minus-two-yard runs
  and conceded safeties in a scripted game. `football_base16` was restarted on it
  and trained to 6.67M / 7.34M / 667k steps (`results/football_base16/MANIFEST.md`).
- **Throwaways and intentional grounding**, and a `Sack` outcome.
- **The officials** (`Systems_PenaltySystem`) flag holding, pass interference,
  roughing, facemask, block in the back, kick-catch interference and grounding from
  what the bodies did, with an official's chance. Training pays a flag in reward
  (`FOUL_REWARD`); a game enforces it with accept/decline by expected points.
- **A pre-snap** of `PRESNAP_TICKS` with the bodies FROZEN — a false start or an
  offside is a drawn flinch (`Systems_IDisciplineModel`, game only), refereed.
  Since revision 15 the flinch lands on a lineman in proportion to his
  indiscipline, and the defense's front is called in the same window.
- **The game layer**: out-of-bounds clock rules, the two-minute warning, kneel and
  spike, played tries, and the NFL's possession-owed overtime.

The first played games on revision 14 (scripted players) read FINAL lines with every
branch exercised — kickoffs returned and touched back, punts, field goals, extra
points, interceptions returned, safeties, flags accepted and declined — and are
recorded in `results/realism/`. Re-measure on the fixed seeds before tuning.

Revision 14, as written on THE OTHER MACHINE (2026-10-04, commit `e1c86a1`),
changes the shapes — 41 vector observations, Quarterback
branches `[8, 2]`, and a Defense branch `[9]` — so the gate refuses an older brain as
well as the stamp. Four things were batched, because nothing was promoted against
revision 13 to lose:

- **Player traits.** `Systems_RoleTable` was keyed on role alone, so both receivers
  and all five linemen were one body. `Systems_Roster` now draws a
  `Systems_PlayerTraits` per formation slot — speed ±5%, mass ±8%, turn rate ±10%,
  and discipline — and every player observes its own four. **A played game keeps
  its roster; training redraws it every episode**, so a brain is fitted across the
  range and not against twenty-two fixed bodies. Same dynamics as a function of the
  traits in both modes, and the traits are observed.
- **`PassDeep`**, an eighth play call. The receivers' separation reward is paid
  short of `DEEP_SHOT_MIN_YARDS` on `Pass` and beyond it on `PassDeep`
  (`Reward_Role.OnRoute`), which is the whole difference between the two calls. The
  scripted quarterback calls it on exactly the plays it used to treat as deep shots.
- **The defense calls its front.** It was a uniform draw. The middle linebacker's
  branch names one of the eight fronts before the snap and
  `Systems_EpisodeDirector` re-forms the defense. (Its own snap-wait,
  `PRE_SNAP_MAX_TICKS`, did not survive the merge: revision 15 calls the front
  inside `PRESNAP_TICKS`.) The other ten defenders share the brain and so the branch, pinned to
  "no call" by the mask. The scripted defense calls the front it was drawn, so the
  scripted game is unchanged. Watch `DefenseCall/<front>` for a collapse: nothing
  penalizes repetition yet.
- **The two lines grip.** `LINE_LINEAR_DAMPING` is 2.4 against everyone else's 0.8
  (`Systems_RoleTable.LinearDampingOf`), with the drive force re-derived, so a
  lineman's top speed is unchanged but he reaches it in three steps, pushes three
  times as hard and stops where he is pushed. At 0.8 two linemen leaning on each
  other kept whatever velocity the shove gave them and five blocks drifted across
  the field as one clump.

**Pre-snap fouls arrived in the same change and are NOT contract.** They were
rolled between plays from the roster's discipline; revision 15 replaced the roll
with this machine's refereed flinch and kept the discipline weighting.

**The pace moved too, and is not contract either** (`Systems_GameRules`): quarters
are 120 s with a 2.5 s huddle — the same ~75 snaps the 300 s / 12 s pairing gave —
and the dead ball is 60 ticks, not 140, so a full game is about eight minutes.
**There is no instant replay any more**: `Systems_ReplayView` still records every
play, but only for the three-highlight reel at the final whistle. And the field
keeps what happened on it — `Systems_TurfScuffView` draws one field-sized wear
texture over the grass, in sand, with a footprint per running player five times a
second, a scuff at every hard cut and a worn patch at every tackle, none of which
fades.

Four scripted games on revision 14, on the tool's four fixed seeds, read **4.84
yards per play and 0.22 TDs per drive**
(`results/realism/realism-20261004-133229.json`: 5.67, 3.85, 5.46, 4.38), inside
both bands, with 84-94 scrimmage plays. The same four seeds read 5.51 and 0.22
before the line grip went in (`realism-20261004-130850.json`), so the lines holding
costs the scripted offense about two-thirds of a yard a play — the first balance
change in this project measured like for like. Neither is comparable with revision
13's 5.89, which was three random games.

**`football_base15` cannot be promoted.** It was a revision 13 run and its
`results/` directory was not on the machine revision 14 was written on. Its config
is in `Config/archive/`.

Two ideas were examined in the same pass and deliberately NOT built there, because
the code already did them: a forward-progress whistle — which THIS machine's
revision 14 built anyway, together with the crowd rule, after measuring a
speed-only stall miss scrums that crept backwards — and speed-dependent
turning (steering only rotates the body; the velocity turns through the drive
force, which already caps lateral acceleration at `TopSpeedOf * LINEAR_DAMPING`,
about 0.75 g for a receiver).

Revision 13 (2026-10-03, `Config/archive/FootballBase15.yaml`) makes execution fallible, with every shape left identical,
so only the stamp refuses an older brain. Through revision 12 being in position was
the whole of every skill — a ball inside `CATCH_RADIUS` was caught, a defender
inside `PASS_BREAKUP_RADIUS` broke it up, a hit over `TACKLE_CLOSING_SPEED` was a
tackle, a held wrap-up was a tackle — and `football_base14` completed 91-97% of its
passes from first summary to last. `Systems_ISkillModel` (`Systems_SeededSkillModel`,
its own stream off `Systems_EpisodeSeed`) now draws for each of them:

- **the throw** is rotated off its line by a triangular error that grows with
  distance and fatigue, applied to the BALL in `Systems_BallSystem.Throw`, for every
  passer. It used to be applied to the scripted quarterback's aim, upstream of
  `PASS_AIM_SLACK`, which discarded 85% of it, and a trained quarterback got none;
- **an open catch** can be dropped, **a contested one** is broken up with a chance
  that rises from 0.45 at the edge of the defender's reach to 0.95 on the ball, and
  **an interception** is held 60% of the time;
- **a hit** can miss (the contact still counts toward the wrap-up) and **a wrap-up**
  can be broken, which restarts its count — likelier for a power back, against one
  man, and when it was all reach and no body, an arm tackle.

**It is the same draw in training and in a played game**, unlike the kick and fumble
models, which keep training a pure function. Those price a decision; these price
execution, and a brain fitted against tackles that never miss would meet different
dynamics the moment a game gave it one. The chances are under "Skill error" in
`Systems_SimConstants`, and the referee tests pin the rules with a stub that never
fails.

Three scripted games on revision 13 read **5.89 yards per play and 0.20 TDs per
drive** (`results/realism/realism-20261003-163114.json`: 4.84, 8.62, 4.22) against
8.02 and 0.37 for the same scripted players on revision 12 — inside both bands, with
interceptions down from 3-11 a game to 2 and stalled plays unchanged at 22-31. Both
measurements are three RANDOM seeds, and that 4.2-8.6 spread is the noise: re-measure
on the four fixed seeds before tuning any of the chances.

Revision 12 (2026-10-03, `Config/archive/FootballBase14.yaml`) changed eight observations and the fatigue dynamics with
every shape left identical — still 36 floats — so only the stamp refuses an older
brain. The ball's offset and velocity are in the body's frame; the two sideline
distances became own spin and the play clock; fatigue is **rested between plays
(60% carried), not cleared**, its gain rose 0.030 → 0.050, and the contact impulse
the solver resolved against a body is a third load term. The reward moved in the
same change and is not contract: a play that runs out the clock is priced as a
tackle, reaching the line to gain pays `FIRST_DOWN_REWARD`, and `COMPLETION_REWARD`
fell 0.6 → 0.45 (0.4 is the floor — below it an incompletion outpays a short
catch). `Agent_ActionContract` and the FootballBase14 header have the evidence.

Three scripted games on revision 12 read 8.02 yards per play and 0.37 TDs per
drive (`results/realism/realism-20261003-140511.json`) against revision 11's 7.25
and 0.33 — but on three random seeds against four fixed ones, so that is inside
single-game noise and is NOT a like-for-like measurement. Re-measure on the fixed
seeds before reading anything into it.

**`football_base13` IS A REVISION 11 RUN AND MUST NOT BE PROMOTED FROM A REVISION
12 TREE.** It was started 2026-10-03 from commit `278e8c6` and its shapes match, so
`promote_brain.py` would pass it and Build Brain Table would stamp it 12. To
promote or realism-check it, check out the commit it was trained at.
`Config/FootballBase13.yaml` stays out of `Config/archive/` only until that run is
finished with.

Revision 11 (FootballBase13) changed two RULES, shapes untouched: a defender within 1.5 m beside or
behind the carrier counts as contact for the wrap-up (`Systems_Referee
.CountPursuitReach`), and a defender within 1.5 m of the ball breaks up a catch
(then `Systems_BallSystem.IsContestedByDefense`; a chance since revision 13). On the scripted players over four
fixed seeds that took the game from 9.13 to 7.25 yards per play, 0.42 to 0.33 TDs
per drive and 69% to 62% completions; the FootballBase13 header has the table.

Revision 10 (FootballBase12, now archived) was: Revision 9 gave the quarterback's aim precision an
effect (a pass can now miss); no run was ever made against it. Revision 10 batched
four more contract changes on top — rear-facing rays (52 -> 84 ray floats, now
fixed in `Sensor_RayContract` rather than the scenes), body-frame own velocity, and
masks that pin every play-call decision that cannot latch and every throw
`HandleQuarterback` would refuse. `Agent_ActionContract` lists them.
*"results/football_long01 is fitted against revision 8 and must not load"* still
holds: the 2.6M-step run that concluded the optimization loop is **not
promotable**.

The reward moved in the same change: flight yardage is paid on the catch, not in
the air (an incomplete forty-yard heave used to net the offense +0.30, more than any
run that ended in a tackle), a block no longer outpays the time cost, and there is
no pursuit reward while the ball is in the air.

Every config for an older contract is in `Config/archive/`, including
FootballBase06-12 and 14-22, FootballLong01 and the six `experiments/`
variants. Until a revision 17 run is promoted, every player is a heuristic.
