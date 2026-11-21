# Judgements, Gauges, and Scoring

[Back to gameplay guide](./gameplay.md) | [中文](./judgements.zh-CN.md)

## beatoraja

<a id="beatoraja-scope"></a>

### Scope

This section documents the beatoraja-compatible judgement and gauge rules used by osu!BMS by default.
5K and 7K include their single-play, 2P, and double-play layouts. PMS covers Pop’n 5K, 9K, 9K 2P, and 9K double play;
KEYBOARD covers 24K, 24K double play, and generic keyboard layouts.

<a id="beatoraja-timing-windows"></a>

### Judgement Tiers and Timing Windows

Timing offsets are in milliseconds relative to the note time: negative means early, positive means late.
$\pm a$ means $[-a,+a]$. PGREAT, GREAT, GOOD, and BAD include both endpoints; the best matching judgement wins.

| Judgement | EX points | Combo                                     | Handling                                                                  |
|-----------|-----------|-------------------------------------------|---------------------------------------------------------------------------|
| PGREAT    | 2         | +1                                        | Completes the scoring judgement.                                          |
| GREAT     | 1         | +1                                        | Completes the scoring judgement.                                          |
| GOOD      | 0         | +1                                        | Completes the scoring judgement.                                          |
| BAD       | 0         | Reset                                     | Completes the judgement in 5K, 7K, and KEYBOARD; PMS allows a rehit.      |
| POOR      | 0         | Reset                                     | A missed deadline, CN/HCN drop, or timeout.                               |
| E-POOR    | 0         | Reset in 5K/PMS; preserved in 7K/KEYBOARD | Empty-press gauge damage; an early empty press leaves the note available. |

“Head” applies to both normal notes and long-note starts; “tail” gives the release or reverse-press windows.
A CN/HCN tail's late action is also limited by the **POOR deadline** in the table: passing it automatically gives POOR.
An LN held through its tail settles automatically after the tail.
For example, a 5K `RANK 2` key tail has a BAD window of ±187.5 and automatically gives POOR past +112.5 on the late
side.

#### RANK and EXRANK

| `#RANK` | Name      | 5K / 7K / KEYBOARD multiplier | PMS multiplier |
|---------|-----------|-------------------------------|----------------|
| 0       | Very Hard | 0.25                          | 0.33           |
| 1       | Hard      | 0.50                          | 0.50           |
| 2       | Normal    | 0.75                          | 0.70           |
| 3       | Easy      | 1.00                          | 1.00           |
| 4       | Very Easy | 1.25                          | 1.33           |

The tables below list every RANK with default judgement settings. In 5K, 7K, and KEYBOARD, PGREAT through BAD scale
with the multiplier; E-POOR stays fixed. PMS fixes PGREAT, BAD, and E-POOR. Its scaled GREAT and GOOD windows are
bounded by PGREAT and BAD for the same note position, with each tier containing the previous tier.

`#DEFEXRANK` and unnumbered `#EXRANK` override the initial RANK with a percentage; `100` equals `RANK 2`.
Channel `A0` applies indexed `#EXRANKxx` values to subsequent notes from the event onward.
The multiplier is $\frac{\mathrm{EXRANK}}{100}\cdot0.75$ for 5K / 7K / KEYBOARD, or $\frac{\mathrm{EXRANK}}{100}\cdot0.70$ for PMS.
An explicit `0` uses a zero multiplier; PMS retains its fixed windows.
Scaled boundaries are truncated toward zero to a precision of 0.001 ms.
For example, a 7K key head at `EXRANK 75` uses PGREAT ±11.25, GREAT ±33.75, GOOD ±84.375, and BAD [-123.75, +157.5].

#### 5K

| Object       | RANK | PGREAT | GREAT  | GOOD   | BAD    | POOR deadline |
|--------------|------|--------|--------|--------|--------|---------------|
| Key head     | 0    | ±5     | ±12.5  | ±25    | ±37.5  | > +37.5       |
| Key head     | 1    | ±10    | ±25    | ±50    | ±75    | > +75         |
| Key head     | 2    | ±15    | ±37.5  | ±75    | ±112.5 | > +112.5      |
| Key head     | 3    | ±20    | ±50    | ±100   | ±150   | > +150        |
| Key head     | 4    | ±25    | ±62.5  | ±125   | ±187.5 | > +187.5      |
| Scratch head | 0    | ±7.5   | ±15    | ±27.5  | ±40    | > +40         |
| Scratch head | 1    | ±15    | ±30    | ±55    | ±80    | > +80         |
| Scratch head | 2    | ±22.5  | ±45    | ±82.5  | ±120   | > +120        |
| Scratch head | 3    | ±30    | ±60    | ±110   | ±160   | > +160        |
| Scratch head | 4    | ±37.5  | ±75    | ±137.5 | ±200   | > +200        |
| Key tail     | 0    | ±30    | ±37.5  | ±50    | ±62.5  | > +37.5       |
| Key tail     | 1    | ±60    | ±75    | ±100   | ±125   | > +75         |
| Key tail     | 2    | ±90    | ±112.5 | ±150   | ±187.5 | > +112.5      |
| Key tail     | 3    | ±120   | ±150   | ±200   | ±250   | > +150        |
| Key tail     | 4    | ±150   | ±187.5 | ±250   | ±312.5 | > +187.5      |
| Scratch tail | 0    | ±32.5  | ±40    | ±40    | ±65    | > +40         |
| Scratch tail | 1    | ±65    | ±80    | ±80    | ±130   | > +80         |
| Scratch tail | 2    | ±97.5  | ±120   | ±120   | ±195   | > +120        |
| Scratch tail | 3    | ±130   | ±160   | ±160   | ±260   | > +160        |
| Scratch tail | 4    | ±162.5 | ±200   | ±200   | ±325   | > +200        |

A 5K long-scratch tail has identical GOOD and GREAT windows; overlapping hits receive GREAT.

#### 7K

| Object       | RANK | PGREAT | GREAT  | GOOD   | BAD              | POOR deadline |
|--------------|------|--------|--------|--------|------------------|---------------|
| Key head     | 0    | ±5     | ±15    | ±37.5  | [-55, +70]       | > +70         |
| Key head     | 1    | ±10    | ±30    | ±75    | [-110, +140]     | > +140        |
| Key head     | 2    | ±15    | ±45    | ±112.5 | [-165, +210]     | > +210        |
| Key head     | 3    | ±20    | ±60    | ±150   | [-220, +280]     | > +280        |
| Key head     | 4    | ±25    | ±75    | ±187.5 | [-275, +350]     | > +350        |
| Scratch head | 0    | ±7.5   | ±17.5  | ±40    | [-57.5, +72.5]   | > +72.5       |
| Scratch head | 1    | ±15    | ±35    | ±80    | [-115, +145]     | > +145        |
| Scratch head | 2    | ±22.5  | ±52.5  | ±120   | [-172.5, +217.5] | > +217.5      |
| Scratch head | 3    | ±30    | ±70    | ±160   | [-230, +290]     | > +290        |
| Scratch head | 4    | ±37.5  | ±87.5  | ±200   | [-287.5, +362.5] | > +362.5      |
| Key tail     | 0    | ±30    | ±40    | ±50    | [-55, +70]       | > +70         |
| Key tail     | 1    | ±60    | ±80    | ±100   | [-110, +140]     | > +140        |
| Key tail     | 2    | ±90    | ±120   | ±150   | [-165, +210]     | > +210        |
| Key tail     | 3    | ±120   | ±160   | ±200   | [-220, +280]     | > +280        |
| Key tail     | 4    | ±150   | ±200   | ±250   | [-275, +350]     | > +350        |
| Scratch tail | 0    | ±32.5  | ±42.5  | ±52.5  | [-57.5, +72.5]   | > +72.5       |
| Scratch tail | 1    | ±65    | ±85    | ±105   | [-115, +145]     | > +145        |
| Scratch tail | 2    | ±97.5  | ±127.5 | ±157.5 | [-172.5, +217.5] | > +217.5      |
| Scratch tail | 3    | ±130   | ±170   | ±210   | [-230, +290]     | > +290        |
| Scratch tail | 4    | ±162.5 | ±212.5 | ±262.5 | [-287.5, +362.5] | > +362.5      |

#### PMS

| Object   | RANK | PGREAT | GREAT  | GOOD    | BAD  | POOR deadline |
|----------|------|--------|--------|---------|------|---------------|
| Key head | 0    | ±20    | ±20    | ±38.61  | ±183 | > +183        |
| Key head | 1    | ±20    | ±25    | ±58.5   | ±183 | > +183        |
| Key head | 2    | ±20    | ±35    | ±81.9   | ±183 | > +183        |
| Key head | 3    | ±20    | ±50    | ±117    | ±183 | > +183        |
| Key head | 4    | ±20    | ±66.5  | ±155.61 | ±183 | > +183        |
| Key tail | 0    | ±120   | ±120   | ±120    | ±283 | > +183        |
| Key tail | 1    | ±120   | ±120   | ±120    | ±283 | > +183        |
| Key tail | 2    | ±120   | ±120   | ±151.9  | ±283 | > +183        |
| Key tail | 3    | ±120   | ±150   | ±217    | ±283 | > +183        |
| Key tail | 4    | ±120   | ±199.5 | ±283    | ±283 | > +183        |

PMS tail PGREAT is fixed at ±120 and BAD at ±283, with a late POOR deadline of +183. Fully overlapping tiers receive the
better judgement.

#### KEYBOARD

| Object   | RANK | PGREAT         | GREAT          | GOOD         | BAD          | POOR deadline |
|----------|------|----------------|----------------|--------------|--------------|---------------|
| Key head | 0    | ±7.5           | ±22.5          | ±50          | [-60, +80]   | > +80         |
| Key head | 1    | ±15            | ±45            | ±100         | [-120, +160] | > +160        |
| Key head | 2    | ±22.5          | ±67.5          | ±150         | [-180, +240] | > +240        |
| Key head | 3    | ±30            | ±90            | ±200         | [-240, +320] | > +320        |
| Key head | 4    | ±37.5          | ±112.5         | ±250         | [-300, +400] | > +400        |
| Key tail | 0    | [-6.25, +40]   | [-18.75, +50]  | [-35, +65]   | [-60, +80]   | > +80         |
| Key tail | 1    | [-12.5, +80]   | [-37.5, +100]  | [-70, +130]  | [-120, +160] | > +160        |
| Key tail | 2    | [-18.75, +120] | [-56.25, +150] | [-105, +195] | [-180, +240] | > +240        |
| Key tail | 3    | [-25, +160]    | [-75, +200]    | [-140, +260] | [-240, +320] | > +320        |
| Key tail | 4    | [-31.25, +200] | [-93.75, +250] | [-175, +325] | [-300, +400] | > +400        |

#### Empty Presses (E-POOR)

The fixed empty-press candidate ranges exclude the left endpoint and include the right. An unjudged note first
matches PGREAT through BAD, so the table also lists the effective early empty-press interval at `RANK 2`.

| Layout / lane | Empty-press candidate range | RANK 2 early empty-press interval | Extra presses near a judged note                  |
|---------------|-----------------------------|-----------------------------------|---------------------------------------------------|
| 5K key        | (-500, +150]                | (-500, -112.5)                    | Can trigger                                       |
| 5K scratch    | (-500, +160]                | (-500, -120)                      | Can trigger                                       |
| 7K key        | (-500, +150]                | (-500, -165)                      | Can trigger                                       |
| 7K scratch    | (-500, +160]                | (-500, -172.5)                    | Can trigger                                       |
| PMS           | (-500, +175]                | (-500, -183)                      | Waits for a rehit or miss after the first mistake |
| KEYBOARD      | (-500, +200]                | (-500, -180)                      | Can trigger                                       |

An offset of exactly -500 ms does not trigger an empty press. For example, 7K `RANK 2` gives BAD at -165 ms and
early E-POOR at -165.001 ms. An eligible playable note in the same column takes priority over empty-press candidates.

#### PMS Rehits

PMS records the first BAD or E-POOR associated with a note and keeps it available for a GOOD, GREAT, or PGREAT rehit.
Both the first mistake and a successful rehit remain in the results. If the note is then missed, the miss consumes
its scoring position and ends the wait, retaining the first mistake's damage and count.

#### Judgement-Window Mods

| Mod           | Removed judgements |
|---------------|--------------------|
| No Good (NG)  | GOOD               |
| No Great (NE) | GREAT, GOOD        |
| No Bad (NB)   | BAD                |

Other windows and POOR deadlines retain their values; the best remaining matching judgement wins.
For example, +80 ms on a 7K `RANK 2` key gives BAD with NG or NE. NB retains the +210 ms miss deadline.

<a id="beatoraja-course-rules"></a>

#### Course Judgement Constraints

Each stage uses its own layout and RANK / EXRANK windows, then applies course constraints: `no_good` removes GOOD;
`no_great` removes GREAT and GOOD. Declaring both uses `no_great`. Other windows and automatic POOR deadlines
retain their values. `ln`, `cn`, and `hcn` select LN, CN, and HCN respectively, including that mode's scoring counts,
tail actions, and body gauge changes. See [course gauges](#beatoraja-course-gauges) for tier selection, family
overrides,
and HP carry-over.

<a id="beatoraja-note-selection"></a>

### Judgement Selection Algorithms

Open **Settings → BMS → Judgement selection algorithm** to choose which note receives a press when judgement windows
overlap in the same column. The four strategies follow beatoraja's `Combo`, `Duration`, `Lowest`, and `Score`
algorithms:

| Option                             | Selection rule                                                                                                                                                 |
|------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Combo priority (LR2)** — default | Switches to a later note when the current candidate is strictly past its late GOOD boundary and the press is at or after the later note's early GOOD boundary. |
| **Time difference priority (AC)**  | Chooses the eligible note closest to the press time. Equal time differences favour the earlier note.                                                           |
| **Earliest note priority**         | Chooses the earliest eligible note, even if a later note would receive a better judgement.                                                                     |
| **Score priority**                 | Uses the same switching rule as Combo priority, with GREAT boundaries instead of GOOD.                                                                         |

For example, on a 7K `#RANK 2` chart, two notes in the same key column occur at 1000 ms and 1100 ms. A press at 1100 ms
gives the first note GOOD under Combo or Earliest note priority; Time difference or Score priority selects the second
note for PGREAT.

In the same example, Combo priority still selects the first note at 1112.5 ms and switches at 1112.501 ms;
Score priority can switch from 1055 ms. Time difference priority keeps the first note at the 1050 ms tie and selects the
second afterward.

Selection applies to normal notes and long-note heads, including scratch. Each note uses the windows determined by
its layout, RANK/EXRANK, and active judgement-window mods.

The algorithm is fixed at play start; setting changes apply to the next play. Replays use the recorded algorithm.
Replays recorded before this setting existed use the original selection behaviour.

<a id="beatoraja-long-notes"></a>

### Long Notes

#### Modes and Judgements

The chart and the **Long Note (L1)**, **Charge Note (L2)**, or **Hell Charge Note (L3)** mod determine the long-note
mode.

| Mode    | How to play                                       | Judgement                                                                                                                                                                                                           |
|---------|---------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **LN**  | Press at the head and hold through the tail.      | One result per long note. While held, it settles with the head judgement once the tail has passed; an early release combines the head and release judgements, taking the worse result. A very early drop gives BAD. |
| **CN**  | Press at the head and release at the tail.        | Head and tail each contribute their own judgement, EX score, combo, and gauge change. Missing the head gives POOR for both ends.                                                                                    |
| **HCN** | Use CN timing, and keep holding through the body. | Head and tail are scored separately. During the body, holding builds recovery and releasing builds damage; reholding after a mistake can recover gauge.                                                             |

#### Long Scratches

For **CN and HCN long scratches**, the tail action is a new press in the direction opposite to the head. You can
release the original direction within the tail window and then press the opposite direction; the opposite press's
timing determines the tail judgement. You can also press the opposite direction while still holding the first key.
An early reversal completes the tail immediately, so aim the reversal at the tail. Releasing the original direction
before the tail window gives POOR. For an ordinary **LN long scratch**, hold the head's direction through the tail;
an early release of that direction judges the drop.

#### HCN Body Recovery and Damage

HCN recovery and damage build up over time, with held and released time cancelling each other. During a steady hold
or release, gauge changes start after more than 200 ms and continue every 200 ms. Delayed updates apply all changes
due during the elapsed time. A tail judged GOOD or
better keeps recovery active through the remaining body. After a missed head, holding the body can still recover gauge.
After a successful early tail judgement, the remaining HCN body stays anchored at the judgement line and shrinks
until the tail reaches it, then disappears.

Let $B=t_{\mathrm{held}}-t_{\mathrm{released}}$ be total held time minus total released time. Each body change follows this table, using the current gauge's recovery
or damage at the current health.

| Net held time $B$            | Gauge change             | $B$ after the change |
|----------------------------|--------------------------|--------------------|
| $B>200\,\mathrm{ms}$                   | One GREAT recovery $\times0.5$ | $B-200\,\mathrm{ms}$         |
| $B<-200\,\mathrm{ms}$                  | One BAD damage $\times0.5$     | $B+200\,\mathrm{ms}$         |
| $-200\,\mathrm{ms}\le B\le200\,\mathrm{ms}$ | Keep accumulating        | Unchanged          |

For example, a steady hold from the body's start has no recovery at 200 ms, then recovers at 200.001, 400.001, and
600.001 ms.
If the body lasts long enough, three recovery changes have occurred at 600.001 ms.

#### PMS Rehold Grace Period

PMS provides a **200 ms rehold grace period** for an early release that would give BAD or worse. Repress within that
period to continue the hold. Once the period expires, the release is judged using its original timing.

<a id="beatoraja-gauges"></a>

### Gauges

Choose among 6 regular gauges and 3 course gauges through mods; Normal is the default. HP uses 100 for 100%, and
changes are percentage points: -3 takes 80% to 77%.

#### Types and Clear Conditions

| Gauge                                 | Type            | Recovery                                                           | Bar colour                                                            |
|---------------------------------------|-----------------|--------------------------------------------------------------------|-----------------------------------------------------------------------|
| Assist Easy (E2) / Easy (E1) / Normal | Groove          | Distributed by chart TOTAL                                         | Red below 20%, amber below the clear threshold, green at or above it. |
| Hard (H1)                             | Survival        | Chart-limited recovery                                             | Fixed red.                                                            |
| EX Hard (H2)                          | Survival        | Fixed configuration in 5K; chart-limited recovery in other layouts | Fixed purple.                                                         |
| Hazard (H3)                           | Survival        | Fixed recovery as listed below; BAD / POOR remove 100 points       | Fixed gold.                                                           |
| Class (C1)                            | Course survival | Fixed recovery per judgement                                       | Fixed red.                                                            |
| EX Class (C2)                         | Course survival | Fixed recovery per judgement                                       | Fixed purple.                                                         |
| EX Hard Class (C3)                    | Course survival | Fixed recovery per judgement                                       | Fixed gold.                                                           |

Groove gauges can recover throughout the chart and pass by reaching the clear threshold at the end. Survival and
course gauges start at 100, have a maximum of 100, and fail at 0. Groove starting values, limits, and clear thresholds
vary by layout:

| Family   | Gauge            | Initial HP | Minimum | Maximum | Clear HP |
|----------|------------------|------------|---------|---------|----------|
| 5K       | Assist Easy (E2) | 20         | 2       | 100     | ≥ 50     |
| 5K       | Easy (E1)        | 20         | 2       | 100     | ≥ 75     |
| 5K       | Normal           | 20         | 2       | 100     | ≥ 75     |
| 7K       | Assist Easy (E2) | 20         | 2       | 100     | ≥ 60     |
| 7K       | Easy (E1)        | 20         | 2       | 100     | ≥ 80     |
| 7K       | Normal           | 20         | 2       | 100     | ≥ 80     |
| PMS      | Assist Easy (E2) | 30         | 2       | 120     | ≥ 65     |
| PMS      | Easy (E1)        | 30         | 2       | 120     | ≥ 85     |
| PMS      | Normal           | 30         | 2       | 120     | ≥ 85     |
| KEYBOARD | Assist Easy (E2) | 30         | 2       | 100     | ≥ 50     |
| KEYBOARD | Easy (E1)        | 20         | 2       | 100     | ≥ 70     |
| KEYBOARD | Normal           | 20         | 2       | 100     | ≥ 70     |

#### Calculating Gauge Changes

Let $T$ be chart TOTAL and $N$ the number of scoring judgements. Normal notes and LNs count once; CN / HCN heads
and tails count separately. Recovery and limiting coefficients in the tables are:

$$
R = \frac{T}{N}, \qquad
L = \frac{\min\left(\max\left(\frac{2T-320}{N}, 0\right), 0.15\right)}{0.15}
$$

R distributes Groove recovery; L limits positive Hard / EX Hard recovery. Other fixed recovery uses the listed value.

With `#TOTAL` omitted:

$$
T = \begin{cases}
\displaystyle \max\left(260, \frac{7.605N}{0.01N+6.5}\right), & \text{5K / 7K / PMS}, \\
\displaystyle \max\left(300, \frac{7.605(N+100)}{0.01N+6.5}\right), & \text{KEYBOARD}.
\end{cases}
$$

D in the 5K EX Hard row is the damage multiplier, $D=\max(D_T,D_N)$. The TOTAL component is:

| TOTAL range   | $D_T$ |
|---------------|-------|
| $T \ge 240$ | 1     |
| $230 \le T < 240$ | 1.11  |
| $210 \le T < 230$ | 1.25  |
| $200 \le T < 210$ | 1.5   |
| $180 \le T < 200$ | 1.666 |
| $160 \le T < 180$ | 2     |
| $150 \le T < 160$ | 2.5   |
| $130 \le T < 150$ | 3.333 |
| $120 \le T < 130$ | 5     |
| $T < 120$ | 10    |

The note-count component is:

$$
\begin{aligned}
D_N &= 1 + \sum_{i=0}^{8} 0.002 \cdot 2^i \left(n_i - \max\left(N, n_{i+1}\right)\right), \\
n_i &= \left\lfloor \frac{1000}{2^i} \right\rfloor.
\end{aligned}
$$

Per-judgement changes follow. Damage then receives any low-health reduction, and HP is clamped to the gauge's limits.

#### 5K Gauges

| Gauge              | PGREAT | GREAT | GOOD  | BAD  | POOR | E-POOR |
|--------------------|--------|-------|-------|------|------|--------|
| Assist Easy (E2)   | $+R$ | $+R$ | $+0.5R$ | -1.5 | -3   | -0.5   |
| Easy (E1)          | $+R$ | $+R$ | $+0.5R$ | -1.5 | -4.5 | -1     |
| Normal             | $+R$ | $+R$ | $+0.5R$ | -3   | -6   | -2     |
| Hard (H1)          | 0      | 0     | 0     | -5   | -10  | -5     |
| EX Hard (H2)       | 0      | 0     | 0     | $-10D$ | $-20D$ | $-10D$ |
| Hazard (H3)        | 0      | 0     | 0     | -100 | -100 | -100   |
| Class (C1)         | +0.01  | +0.01 | 0     | -0.5 | -1   | -0.5   |
| EX Class (C2)      | +0.01  | +0.01 | 0     | -1   | -2   | -1     |
| EX Hard Class (C3) | +0.01  | +0.01 | 0     | -2.5 | -5   | -2.5   |

#### 7K Gauges

| Gauge              | PGREAT | GREAT  | GOOD   | BAD  | POOR | E-POOR |
|--------------------|--------|--------|--------|------|------|--------|
| Assist Easy (E2)   | $+R$ | $+R$ | $+0.5R$ | -1.5 | -3   | -0.5   |
| Easy (E1)          | $+R$ | $+R$ | $+0.5R$ | -1.5 | -4.5 | -1     |
| Normal             | $+R$ | $+R$ | $+0.5R$ | -3   | -6   | -2     |
| Hard (H1)          | $+0.15L$ | $+0.12L$ | $+0.03L$ | -5   | -10  | -5     |
| EX Hard (H2)       | $+0.15L$ | $+0.06L$ | 0      | -8   | -16  | -8     |
| Hazard (H3)        | +0.15  | +0.06  | 0      | -100 | -100 | -10    |
| Class (C1)         | +0.15  | +0.12  | +0.06  | -1.5 | -3   | -1.5   |
| EX Class (C2)      | +0.15  | +0.12  | +0.03  | -3   | -6   | -3     |
| EX Hard Class (C3) | +0.15  | +0.06  | 0      | -5   | -10  | -5     |

#### PMS Gauges

| Gauge              | PGREAT | GREAT  | GOOD   | BAD  | POOR | E-POOR |
|--------------------|--------|--------|--------|------|------|--------|
| Assist Easy (E2)   | $+R$ | $+R$ | $+0.5R$ | -1   | -2   | -2     |
| Easy (E1)          | $+R$ | $+R$ | $+0.5R$ | -1   | -3   | -3     |
| Normal             | $+R$ | $+R$ | $+0.5R$ | -2   | -6   | -6     |
| Hard (H1)          | $+0.15L$ | $+0.12L$ | $+0.03L$ | -5   | -10  | -10    |
| EX Hard (H2)       | $+0.15L$ | $+0.06L$ | 0      | -10  | -15  | -15    |
| Hazard (H3)        | +0.15  | +0.06  | 0      | -100 | -100 | -100   |
| Class (C1)         | +0.15  | +0.12  | +0.06  | -1.5 | -3   | -3     |
| EX Class (C2)      | +0.15  | +0.12  | +0.03  | -3   | -6   | -6     |
| EX Hard Class (C3) | +0.15  | +0.06  | 0      | -5   | -10  | -10    |

#### KEYBOARD Gauges

| Gauge              | PGREAT | GREAT | GOOD  | BAD  | POOR | E-POOR |
|--------------------|--------|-------|-------|------|------|--------|
| Assist Easy (E2)   | $+R$ | $+R$ | $+0.5R$ | -1   | -2   | -1     |
| Easy (E1)          | $+R$ | $+R$ | $+0.5R$ | -1   | -3   | -1     |
| Normal             | $+R$ | $+R$ | $+0.5R$ | -2   | -4   | -2     |
| Hard (H1)          | $+0.2L$ | $+0.2L$ | $+0.1L$ | -4   | -8   | -4     |
| EX Hard (H2)       | $+0.2L$ | $+0.1L$ | 0     | -6   | -12  | -6     |
| Hazard (H3)        | +0.2   | +0.1  | 0     | -100 | -100 | -100   |
| Class (C1)         | +0.2   | +0.2  | +0.1  | -1.5 | -3   | -1.5   |
| EX Class (C2)      | +0.2   | +0.2  | +0.1  | -3   | -6   | -3     |
| EX Hard Class (C3) | +0.2   | +0.1  | 0     | -5   | -10  | -5     |

#### Low-Health Damage Reduction

For 7K / PMS / KEYBOARD Hard and Class, read from top to bottom and use the first matching condition to multiply
judgement damage:

| Current HP for Hard | Current HP for Class | Damage multiplier |
|---------------------|----------------------|-------------------|
| < 10                | < 5                  | 0.4               |
| < 20                | < 10                 | 0.5               |
| < 30                | < 15                 | 0.6               |
| < 40                | < 20                 | 0.7               |
| < 50                | < 25                 | 0.8               |
| Otherwise           | Otherwise            | 1                 |

Thresholds are strict: Class uses 0.8 at $\mathrm{HP}=20$ and 1 at $\mathrm{HP}=25$. The 5K Hard / Class gauges and these four families'
EX Hard, Hazard, EX Class, and EX Hard Class use their base damage.

#### Mines and Auto Gauge

Holding a lane as a mine crosses the judgement line removes its chart-defined HP. Either scratch direction can
trigger a scratch mine. Damage is clamped to the gauge limits, so Groove retains at least 2 HP and Survival can fail
at 0. See [long notes](#beatoraja-long-notes) for HCN body gauge changes.

Auto Gauge (AG) maintains separate HP for Hazard → EX Hard → Hard → Normal → Easy → Assist Easy, showing the
hardest surviving gauge and dropping to the next after failure. The highest gauge that clears determines the result.

<a id="beatoraja-course-gauges"></a>

#### Course Gauges and Carry-Over

The selected gauge mod maps to a course tier. The first stage starts at 100; subsequent stages inherit remaining HP.
Reaching 0 fails the course.

| Selected gauge                      | Course gauge                 |
|-------------------------------------|------------------------------|
| Assist Easy / Easy / Normal / Class | Class (C1)                   |
| Hard / EX Class                     | EX Class (C2)                |
| EX Hard / Hazard / EX Hard Class    | EX Hard Class (C3)           |
| Auto Gauge                          | Tracks C3 → C2 → C1 together |

Course recovery and base damage use the C1 / C2 / C3 rows for each family above. The current stage's layout selects
the default family; course constraints can specify one family for the entire course:

| Constraint  | Gauge family                  |
|-------------|-------------------------------|
| `gauge_5k`  | 5K                            |
| `gauge_7k`  | 7K                            |
| `gauge_9k`  | PMS                           |
| `gauge_24k` | KEYBOARD                      |
| `gauge_lr2` | LR2 course-gauge family below |

The LR2 course-gauge family uses these changes. Class / EX Class multiply judgement damage by 0.6 below 30 HP;
EX Hard Class uses base damage.

| Gauge              | PGREAT | GREAT | GOOD  | BAD | POOR | E-POOR |
|--------------------|--------|-------|-------|-----|------|--------|
| Class (C1)         | +0.1   | +0.1  | +0.05 | -2  | -3   | -2     |
| EX Class (C2)      | +0.1   | +0.1  | +0.05 | -6  | -10  | -2     |
| EX Hard Class (C3) | +0.1   | +0.1  | +0.05 | -12 | -20  | -2     |

Auto Gauge carries every surviving tier's own HP to the next stage; failed tiers remain failed. These families set
gauge changes, while each stage's note judgements still use its layout, RANK / EXRANK, and course judgement constraints.

Gauge reference: [beatoraja
`GaugeProperty`](https://github.com/exch-bms2/beatoraja/blob/9cddf911113e74da87458344bb85a4ea45431d0b/src/bms/player/beatoraja/play/GaugeProperty.java).

<a id="beatoraja-scoring"></a>

### Scoring and Ranks

#### EX Score and Scoring Counts

$$
\mathrm{EX} = 2N_{\mathrm{PGREAT}} + N_{\mathrm{GREAT}}
$$

| Object      | Scoring judgements | Maximum EX points | Maximum combo increase |
|-------------|--------------------|-------------------|------------------------|
| Normal note | 1                  | 2                 | 1                      |
| LN          | 1                  | 2                 | 1                      |
| CN          | 2 (head and tail)  | 4                 | 2                      |
| HCN         | 2 (head and tail)  | 4                 | 2                      |
| Landmine    | 0                  | 0                 | 0                      |

$$
\mathrm{EX}_{\max} = 2\left(N_{\mathrm{tap}} + N_{\mathrm{LN}} + 2N_{\mathrm{CN}} + 2N_{\mathrm{HCN}}\right)
$$

The $N$ terms count normal notes, LNs, CNs, and HCNs respectively.
HCN body gauge changes are settled separately. E-POOR affects the gauge and the layout's combo policy; hitting a
landmine resets combo.

**osu!BMS normalized score:**

$$
S = 10^6\frac{\mathrm{EX}}{\mathrm{EX}_{\max}}
$$

#### Course Scoring

Each stage scores and builds combo separately. A completed course sums stage EX scores and maximum EX scores,
then grades their combined ratio and judgement counts. The normalized total is the sum of stage normalized scores;
maximum combo is the best individual stage combo. HP carries between stages under the course gauge rules.

#### DJ LEVEL Ranks

Ranks use the ratio of EX score to maximum EX score. S also requires zero recorded GREAT, GOOD, BAD, and POOR
judgements.

| Rank | EX ratio condition                    | Percentage | EX points required with a maximum of 2000 |
|------|---------------------------------------|------------|-------------------------------------------|
| S    | 1, with the judgement condition above | 100%       | 2000                                      |
| AAA  | $\ge \frac{8}{9}$ | ≥ 88.888…% | 1778                                      |
| AA   | $\ge \frac{7}{9}$ | ≥ 77.777…% | 1556                                      |
| A    | $\ge \frac{6}{9}$ | ≥ 66.666…% | 1334                                      |
| B    | $\ge \frac{5}{9}$ | ≥ 55.555…% | 1112                                      |
| C    | $< \frac{5}{9}$ | < 55.555…% | 0–1111                                    |

For rank threshold ratio $q$, the integer EX threshold is $\left\lceil \mathrm{EX}_{\max}q \right\rceil$.

Window reference: [beatoraja
`9cddf911`](https://github.com/exch-bms2/beatoraja/blob/9cddf911113e74da87458344bb85a4ea45431d0b/src/bms/player/beatoraja/play/JudgeProperty.java).
POOR deadlines, empty-press searches, and scoring follow osu!BMS gameplay rules.

## LR2

<a id="lr2-scope"></a>

### Scope

This section documents the LR2oraja-compatible rules used by osu!BMS's **LR2 (R2)** mod in single-chart play and
practice.
All layouts use the same key and scratch windows, and long notes are converted to classic LN.

<a id="lr2-timing-windows"></a>

### Judgement Tiers and Timing Windows

Offsets are in milliseconds relative to the note time: negative means early, positive means late. $\pm a$ means
$[-a,+a]$, including both endpoints. The first matching tier wins in PGREAT, GREAT, GOOD, BAD, E-POOR order.

| Judgement | EX points | Combo     | Handling                                                                           |
|-----------|-----------|-----------|------------------------------------------------------------------------------------|
| PGREAT    | 2         | +1        | Completes the scoring judgement.                                                   |
| GREAT     | 1         | +1        | Completes the scoring judgement.                                                   |
| GOOD      | 0         | +1        | Completes the scoring judgement.                                                   |
| BAD       | 0         | Reset     | Completes the scoring judgement; an LN head BAD immediately settles the entire LN. |
| POOR      | 0         | Reset     | An automatic miss deadline has passed.                                             |
| E-POOR    | 0         | Preserved | Gauge damage; an early empty press leaves the note available.                      |

#### Normal Notes and Long-Note Heads

These are the default judgement windows. BAD is fixed at ±200 ms; R2 treats `#RANK 4` as Normal.

| `#RANK` | Name              | Multiplier $r$ | PGREAT | GREAT | GOOD | BAD  |
|---------|-------------------|--------------|--------|-------|------|------|
| 0       | Very Hard         | 0.25         | ±8     | ±24   | ±40  | ±200 |
| 1       | Hard              | 0.50         | ±15    | ±30   | ±60  | ±200 |
| 2       | Normal            | 0.75         | ±18    | ±40   | ±100 | ±200 |
| 3       | Easy              | 1.00         | ±21    | ±60   | ±120 | ±200 |
| 4       | Treated as Normal | 0.75         | ±18    | ±40   | ±100 | ±200 |

#### Long-Note Tails

PGREAT, GREAT, and GOOD tail windows all use the head's GOOD width. An overlapping release receives PGREAT,
then combines with the head by taking the worse judgement. Holding through an LN's tail settles it automatically
after the tail; actual early-release results are listed under “Long Notes” below. $\ell$ denotes LN duration.

| `#RANK` | PGREAT | GREAT | GOOD | BAD  |
|---------|--------|-------|------|------|
| 0       | ±40    | ±40   | ±40  | ±200 |
| 1       | ±60    | ±60   | ±60  | ±200 |
| 2       | ±100   | ±100  | ±100 | ±200 |
| 3       | ±120   | ±120  | ±120 | ±200 |
| 4       | ±100   | ±100  | ±100 | ±200 |

#### EXRANK

`#DEFEXRANK` and unnumbered `#EXRANK` set the initial multiplier. Indexed `#EXRANKxx` on channel `A0` applies to
subsequent notes from the event onward. The multiplier is $r=\frac{\mathrm{EXRANK}}{100}\cdot0.75$; `EXRANK 100` equals Normal.

PGREAT, GREAT, and GOOD interpolate linearly between adjacent multiplier points below. At $r\ge1$, multiply the
Easy windows by r, with each width capped at 200 ms. Results truncate toward zero to 0.001 ms; BAD and E-POOR stay
fixed.

| Multiplier $r$ | PGREAT width | GREAT width | GOOD width |
|--------------|--------------|-------------|------------|
| 0            | 0            | 0           | 0          |
| 0.25         | 8            | 24          | 40         |
| 0.50         | 15           | 30          | 60         |
| 0.75         | 18           | 40          | 100        |
| 1.00         | 21           | 60          | 120        |

For example, `EXRANK 75` gives $r=0.5625$, with head windows of PGREAT ±15.75, GREAT ±32.5, and GOOD ±70.
An explicit `EXRANK 0` narrows PGREAT, GREAT, and GOOD to 0.

#### Empty Presses (E-POOR)

| State                                                                          | Range                                                                      |
|--------------------------------------------------------------------------------|----------------------------------------------------------------------------|
| Empty-press candidate window                                                   | (-1000, 0]                                                                 |
| Effective early empty-press interval for an unjudged note with default windows | (-1000, -200)                                                              |
| Extra presses near a judged note                                               | Within the candidate window, ending at the original note time, including 0 |

Exactly -1000 ms does not trigger an empty press, and -200 ms is BAD. E-POOR preserves combo in every layout.
An eligible playable note in the same column takes priority over empty-press candidates.

#### Judgement-Window Mods

| Mod           | Removed judgements |
|---------------|--------------------|
| No Good (NG)  | GOOD               |
| No Great (NE) | GREAT, GOOD        |
| No Bad (NB)   | BAD                |

Other windows and automatic miss deadlines retain their values. LN head miss deadlines and tail PGREAT windows
still use the GOOD width from before removal.

<a id="lr2-note-selection"></a>

### Judgement Selection Algorithms

R2 fixes selection to Combo priority: scan notes in time order, switching when the current candidate is strictly
past its late GOOD boundary and the later note has reached its early GOOD boundary. Simultaneous candidates use
chart order. When NG/NE removes GOOD, both switching boundaries become 0.

For example, at Normal, two notes in the same column occur at 1000 and 1160 ms. A press at 1100 ms still judges
the first for GOOD; at 1100.001 ms it switches to the second for GOOD; at 1150 ms it selects the second for PGREAT.

<a id="lr2-long-notes"></a>

### Long Notes

Press at the head and hold through the tail. Each LN contributes one judgement to score and combo; a long scratch
holds the direction pressed at its head.

| Situation                                                                    | Result                                                                       |
|------------------------------------------------------------------------------|------------------------------------------------------------------------------|
| Head PGREAT / GREAT / GOOD, held through the tail                            | Settles after the tail with the head judgement.                              |
| Head BAD                                                                     | Immediately settles the entire LN as BAD.                                    |
| Release within the head's GOOD width before the tail, including the boundary | Keeps the head judgement; Normal allows release from 100 ms before the tail. |
| Release earlier                                                              | Immediately settles as BAD.                                                  |
| Head remains unhit                                                           | One POOR after $t_{\mathrm{head}}+\min(W_{\mathrm{GOOD}},\ell)$.                     |

With default windows, automatic head POOR deadlines are Very Hard +40, Hard +60, Normal +100, and Easy +120 ms.
Shorter LNs use their own duration. `#RANK 4` uses Normal's +100 ms.
For a Normal LN from 1000 to 1500 ms, release at 1400 ms keeps the head judgement, while 1399.999 ms gives BAD.
An unhit head automatically gives POOR after 1100 ms.

<a id="lr2-gauges"></a>

### Gauges

R2 uses the following configuration in every layout. HP uses 100 for 100%, and changes are percentage points.
Assist Easy / Easy / Normal start at 20, have a minimum of 2 and maximum of 100, and clear at 60, 80, and 80
respectively.
Hard / EX Hard / Hazard start at 100 with a maximum of 100. They survive at $\mathrm{HP}\ge2$; falling below 2 sets HP to 0 and
fails.

#### Per-Judgement Changes

Let $T$ be TOTAL, $N$ the converted scoring-judgement count, and $R=\frac{T}{N}$. Each LN counts once. Recovery uses
the listed coefficients; negative Hard / EX Hard changes multiply by damage factor D before low-health reduction.

| Gauge            | PGREAT | GREAT | GOOD  | BAD  | POOR | E-POOR |
|------------------|--------|-------|-------|------|------|--------|
| Assist Easy (E2) | $+1.2R$ | $+1.2R$ | $+0.6R$ | -3.2 | -4.8 | -1.6   |
| Easy (E1)        | $+1.2R$ | $+1.2R$ | $+0.6R$ | -3.2 | -4.8 | -1.6   |
| Normal           | $+R$ | $+R$ | $+0.5R$ | -4   | -6   | -2     |
| Hard (H1)        | +0.1   | +0.1  | +0.05 | $-6D$ | $-10D$ | $-2D$ |
| EX Hard (H2)     | +0.1   | +0.1  | +0.05 | $-12D$ | $-20D$ | $-2D$ |
| Hazard (H3)      | 0      | 0     | 0     | -100 | -100 | 0      |

Groove bar colours follow the clear threshold; Hard is fixed red, EX Hard purple, and Hazard gold. Mines directly
remove their chart-defined HP before applying the limits above. Auto Gauge tracks H3 → H2 → H1 → Normal → E1 → E2,
showing the hardest surviving tier and recording the highest tier that clears at the end.

#### TOTAL and Damage Correction

With `#TOTAL` omitted:

$$
T = 160 + 0.16\left(N + \min\left(\max(N-400, 0), 200\right)\right)
$$

For $N=400$, 500, 600, and 1000, $T$ is 224, 256, 288, and 352 respectively.

Hard / EX Hard use:

$$
D = \max(D_T, D_N), \qquad
D_T = \frac{10}{\min\left(\max\left(\left\lfloor\frac{T}{16}\right\rfloor - 5, 1\right), 10\right)}
$$

The note-count component is:

| Scoring judgements $N$ | $D_N$ |
|----------------------|------------------------|
| $N \le 20$ | 10                     |
| $20 < N < 30$ | $8 + 0.2 \cdot (30 - N)$ |
| $30 \le N < 60$ | $5 + \frac{60-N}{15}$ |
| $60 \le N < 125$ | $4 + \frac{125-N}{65}$ |
| $125 \le N < 250$ | $3 + 0.008 \cdot (250 - N)$ |
| $250 \le N < 500$ | $2 + 0.004 \cdot (500 - N)$ |
| $500 \le N < 1000$ | $1 + 0.002 \cdot (1000 - N)$ |
| $N \ge 1000$ | 1                      |

#### Low-Health Damage Reduction

Hard multiplies corrected judgement damage by 0.6 when pre-judgement HP is below 32; exactly 32 uses full damage.
EX Hard and Hazard use the changes above. For example, with $T=300$ and $N=1000$, Hard BAD has base damage 6:
it removes 6 at $\mathrm{HP}=32$ and 3.6 at $\mathrm{HP}=31.99$. Exactly 2 HP survives; 1.999 becomes 0 and fails.

<a id="lr2-scoring"></a>

### Scoring and Ranks

#### EX Score and Scoring Counts

$$
\mathrm{EX} = 2N_{\mathrm{PGREAT}} + N_{\mathrm{GREAT}}
$$

| Object                             | Scoring judgements | Maximum EX points | Maximum combo increase |
|------------------------------------|--------------------|-------------------|------------------------|
| Normal note                        | 1                  | 2                 | 1                      |
| LN, including converted long notes | 1                  | 2                 | 1                      |
| Landmine                           | 0                  | 0                 | 0                      |

$$
\mathrm{EX}_{\max} = 2\left(N_{\mathrm{tap}} + N_{\mathrm{LN}}\right)
$$

$N_{\mathrm{tap}}$ counts normal notes, and $N_{\mathrm{LN}}$ counts LNs.
E-POOR affects the gauge and preserves combo; hitting a landmine resets combo.

**osu!BMS normalized score:**

$$
S = 10^6\frac{\mathrm{EX}}{\mathrm{EX}_{\max}}
$$

#### DJ LEVEL Ranks

Ranks use the ratio of EX score to maximum EX score. S also requires zero recorded GREAT, GOOD, BAD, and POOR
judgements.

| Rank | EX ratio condition                    | Percentage | EX points required with a maximum of 2000 |
|------|---------------------------------------|------------|-------------------------------------------|
| S    | 1, with the judgement condition above | 100%       | 2000                                      |
| AAA  | $\ge \frac{8}{9}$ | ≥ 88.888…% | 1778                                      |
| AA   | $\ge \frac{7}{9}$ | ≥ 77.777…% | 1556                                      |
| A    | $\ge \frac{6}{9}$ | ≥ 66.666…% | 1334                                      |
| B    | $\ge \frac{5}{9}$ | ≥ 55.555…% | 1112                                      |
| C    | $< \frac{5}{9}$ | < 55.555…% | 0–1111                                    |

For threshold ratio $q$, the integer EX threshold is $\left\lceil \mathrm{EX}_{\max}q \right\rceil$. Scores match the LR2 (R2) mod condition.

Base window reference: [LR2oraja
`c1c8e4e0`](https://github.com/wcko87/lr2oraja/blob/c1c8e4e0148abd318a9375de1631227cc383d0e4/src/bms/player/beatoraja/play/JudgeProperty.java).
Continuous EXRANK interpolation, automatic deadlines,
and scoring follow osu!BMS's R2 gameplay rules.

<a id="osu-mania"></a>

## osu!mania (OM)

<a id="osu-mania-scope"></a>

### Scope

The **osu!mania (OM)** mod switches judgement, health, and scoring to osu!mania rules. **Use lazer rules** is enabled
by default; disabling it selects stable. Both convert long notes to mania holds and remove landmines. Keys and
scratch use the same timing windows.

| Setting                 | Range          | Default                                           |
|-------------------------|----------------|---------------------------------------------------|
| Overall difficulty (OD) | 0–10, step 0.1 | Original OD for mania-source charts; 8 otherwise. |
| HP drain                | 0–10, step 0.1 | Original HP for mania-source charts; 8 otherwise. |
| Use lazer rules         | On / off       | On.                                               |

OM applies to single-chart play and practice. The following sections distinguish lazer from stable.

<a id="osu-mania-timing-windows"></a>

### Judgement Tiers and Timing Windows

Offsets are in milliseconds relative to note time: negative means early and positive means late. $\pm a$ means
$[-a,+a]$, including both endpoints. The first matching tier wins in MAX, 300, 200, 100, 50, MISS order.

| Display | Judgement | lazer accuracy weight | stable accuracy weight | Combo |
|---------|-----------|-----------------------|------------------------|-------|
| MAX     | PERFECT   | 305                   | 300                    | +1    |
| 300     | GREAT     | 300                   | 300                    | +1    |
| 200     | GOOD      | 200                   | 200                    | +1    |
| 100     | OK        | 100                   | 100                    | +1    |
| 50      | MEH       | 50                    | 50                     | +1    |
| MISS    | MISS      | 0                     | 0                      | Reset |

#### OD Formulas

Let $d$ be OD. Calculate the base half-width $b$, then use $W=\lfloor b\rfloor+0.5$ for the window half-width.

| Judgement | lazer b                                | stable b |
|-----------|----------------------------------------|----------|
| MAX       | $d \le 5:\ 22.4 - 0.6d;\quad d > 5:\ 24.9 - 1.1d$ | 16       |
| 300       | $64 - 3d$ | $64 - 3d$ |
| 200       | $97 - 3d$ | $97 - 3d$ |
| 100       | $127 - 3d$ | $127 - 3d$ |
| 50        | $151 - 3d$ | $151 - 3d$ |
| MISS      | $188 - 3d$ | $188 - 3d$ |

#### Normal-Note and Head Windows

These windows use chart time. Both rules share the 300 through MISS values. OM keeps chart-time windows fixed;
At playback rate $r$, the real-time half-width is $W_{\mathrm{real}}=\frac{W_{\mathrm{chart}}}{r}$. lazer hold heads
use the same table; stable holds use the combined-endpoint rules below. Automatic miss deadlines are listed separately.

| OD | MAX (lazer) | MAX (stable) | 300   | 200   | 100    | 50     | MISS   |
|----|-------------|--------------|-------|-------|--------|--------|--------|
| 0  | ±22.5       | ±16.5        | ±64.5 | ±97.5 | ±127.5 | ±151.5 | ±188.5 |
| 1  | ±21.5       | ±16.5        | ±61.5 | ±94.5 | ±124.5 | ±148.5 | ±185.5 |
| 2  | ±21.5       | ±16.5        | ±58.5 | ±91.5 | ±121.5 | ±145.5 | ±182.5 |
| 3  | ±20.5       | ±16.5        | ±55.5 | ±88.5 | ±118.5 | ±142.5 | ±179.5 |
| 4  | ±20.5       | ±16.5        | ±52.5 | ±85.5 | ±115.5 | ±139.5 | ±176.5 |
| 5  | ±19.5       | ±16.5        | ±49.5 | ±82.5 | ±112.5 | ±136.5 | ±173.5 |
| 6  | ±18.5       | ±16.5        | ±46.5 | ±79.5 | ±109.5 | ±133.5 | ±170.5 |
| 7  | ±17.5       | ±16.5        | ±43.5 | ±76.5 | ±106.5 | ±130.5 | ±167.5 |
| 8  | ±16.5       | ±16.5        | ±40.5 | ±73.5 | ±103.5 | ±127.5 | ±164.5 |
| 9  | ±15.5       | ±16.5        | ±37.5 | ±70.5 | ±100.5 | ±124.5 | ±161.5 |
| 10 | ±13.5       | ±16.5        | ±34.5 | ±67.5 | ±97.5  | ±121.5 | ±158.5 |

#### Empty Presses and Automatic Misses

For normal notes, a press before a candidate's MISS window has no judgement. Entering its early MISS window and pressing
consumes the note as MISS. Empty presses around a resolved note cause no damage.

| Rules  | Automatic MISS for a normal note                                                      |
|--------|---------------------------------------------------------------------------------------|
| lazer  | Past the late 50 boundary: > +127.5 ms at OD 8.                                       |
| stable | At $\lfloor127-3d\rfloor$ ms: ≥ +103 ms at OD 8, so pressing at +103 ms is already a miss. |

For example, lazer OD 8 gives MISS at -164.5 ms, 50 at -127.5 ms, and automatic MISS past +127.5 ms.
stable OD 8 allows early 50 hits, while the late side automatically misses at +103 ms.

<a id="osu-mania-note-selection"></a>

### Judgement Selection Algorithms

Both rules process eligible notes in time order within a lane. lazer triggers on a lane changing from released to
held; release and press again for the next hit. Once a press reaches a later note in the lane and falls within that
note's judgement range, lazer marks earlier, already-ended unresolved notes as MISS before judging the later note.

A stable tap judged early keeps its lane occupied until the note's scheduled time; additional presses before then
are still handled by that note. Releasing a held note takes priority over press candidates.

<a id="osu-mania-long-notes"></a>

### Long Notes

#### lazer

Head and tail each contribute one judgement, independently affecting score, accuracy, combo, and health. Tail
release windows are **1.5 times** the corresponding head windows.

| OD 8 tail judgement | Window  |
|---------------------|---------|
| MAX                 | ±24.75  |
| 300                 | ±60.75  |
| 200                 | ±110.25 |
| 100                 | ±155.25 |
| 50                  | ±191.25 |
| MISS                | ±246.75 |

The head automatically misses past +127.5 ms, and the tail past +191.25 ms. Dropping a hold resets combo; reholding
allows the tail to be played. After a drop or missed head, a successful tail is capped at 50. Holding past the tail
still requires a release; exceeding its deadline gives MISS. At OD 8, reholding is accepted through tail +127.5 ms,
inclusive, followed by a release within tail +191.25 ms.

#### stable

Each hold contributes one score and accuracy judgement, combining press and release timing. Input times use integer
milliseconds. Let $a$ be the absolute error of the last accepted press relative to the head, $b$ the absolute error of
an accepted release relative to the tail, and $w$ the floored base half-width from the OD formulas, without adding 0.5.
Automatic settlement uses its settlement-time error for $b$.

| Judgement | Conditions                                                               | OD 8 values                             |
|-----------|--------------------------------------------------------------------------|-----------------------------------------|
| MAX       | $a \le 1.2w_{\mathrm{MAX}}\quad\land\quad a + b \le 2.4w_{\mathrm{MAX}}$ | $a \le 19.2;\quad a + b \le 38.4$ |
| 300       | $a \le 1.1w_{300}\quad\land\quad a + b \le 2.2w_{300}$ | $a \le 44;\quad a + b \le 88$ |
| 200       | $a \le w_{200}\quad\land\quad a + b \le 2w_{200}$ | $a \le 73;\quad a + b \le 146$ |
| 100       | $a \le w_{100}\quad\land\quad a + b \le 2w_{100}$ | $a \le 103;\quad a + b \le 206$ |
| 50        | Held effectively into the tail window without meeting a better condition | The tail uses a 127 ms 50 half-width.   |
| MISS      | No effective hold in the tail's resolution interval                      | When released, settles at tail +103 ms. |

Releasing before the tail's early 50 boundary immediately breaks combo; reholding can recover the hold, with its
final judgement capped at 200 after a drop. Releases are accepted within $[-w_{50},+w_{100})$, or [-127, +103) ms at OD 8.
At +103 ms, a released hold gives MISS; a still-held hold waits until +127 ms, retaining that pending result if released afterward.
A still-held note waits until the late 50 boundary to settle: with an
exact head at OD 8, holding through tail +127 ms gives 200. For a head 10 ms late and tail 30 ms late, stable gives one
300; lazer gives
head MAX and tail 300.
Reholding recalculates $a$ from that press. With no accepted press, or a last press before the head's early 50 boundary,
a becomes $\lvert\ell-1\,\mathrm{ms}\rvert$.

While held, stable adds a body tick every **100 ms**, starting at $\max(t_{\mathrm{head}},t_{\mathrm{press}}+100\,\mathrm{ms})$. Each tick adds
one combo and recovers health. Releasing stops ticks, and reholding schedules them again, so tick counts and the
maximum reachable combo depend on actual press and release timing.

<a id="osu-mania-gauges"></a>

### Gauges

Health starts at 100%, is capped at 100%, and fails at 0%. The bar is white and changes with judgements during play.
Let $h$ be HP difficulty and $C$ the recovery multiplier calibrated from chart duration, note distribution, breaks, and h.
Positive judgement recovery multiplies by C before being capped at full health. Changes below are percentage points.

| Judgement / event                   | Formula          | HP 0   | HP 5   | HP 8   | HP 10  |
|-------------------------------------|------------------|--------|--------|--------|--------|
| MAX                                 | $+(0.55 - 0.05h)C$ | $+0.55C$ | $+0.3C$ | $+0.15C$ | $+0.05C$ |
| 300                                 | $+(0.5 - 0.05h)C$ | $+0.5C$ | $+0.25C$ | $+0.1C$ | 0      |
| 200                                 | $+(0.4 - 0.04h)C$ | $+0.4C$ | $+0.2C$ | $+0.08C$ | 0      |
| 100                                 | 0                | 0      | 0      | 0      | 0      |
| 50                                  | $-0.16(h + 1)$ | -0.16  | -0.96  | -1.44  | -1.76  |
| Normal-note MISS / stable hold MISS | $-0.75(h + 1)$ | -0.75  | -4.5   | -6.75  | -8.25  |
| One lazer hold endpoint MISS        | $-0.375(h + 1)$ | -0.375 | -2.25  | -3.375 | -4.125 |
| stable body tick                    | $+(0.25 - 0.025h)$ | +0.25  | +0.125 | +0.05  | 0      |

Missing both lazer hold endpoints deals the same total MISS damage as a normal note. A separate break event only
resets combo; health follows subsequent judgements and body ticks. C depends on the chart and lazer / stable hold
scoring, so actual recovery can differ at the same HP difficulty. Body ticks use the listed recovery directly.

<a id="osu-mania-scoring"></a>

### Scoring and Ranks

#### Scoring Counts and Accuracy

| Object      | lazer                                  | stable                                                            |
|-------------|----------------------------------------|-------------------------------------------------------------------|
| Normal note | 1 judgement                            | 1 judgement                                                       |
| Hold        | Head and tail separately: 2 judgements | 1 judgement for the whole hold                                    |
| Body ticks  | 0 judgements                           | Add combo and health; the whole hold supplies score and accuracy. |

Let $N$ be total scoring judgements, $n$ the resolved count, and $w_j$ the accuracy weight of judgement $j$:

$$
A_{\mathrm{lazer}} = \frac{\sum_{j=1}^{n} w_j}{305n}, \qquad
A_{\mathrm{stable}} = \frac{\sum_{j=1}^{n} w_j}{300n}
$$

MAX and 300 both give 100% stable accuracy; lazer 300 gives
$\frac{300}{305}\approx98.3607\%$.

#### lazer Score

The maximum is 1,000,000: 15% from combo and 85% from accuracy. Let $A$ be current accuracy, $p=\frac{n}{N}$,
and $c$ combo after the judgement. The combo factor is:

$$
f(c) = \min\left(\max\left(0.5, \log_4\left(\max(1,c)\right)\right), \log_4 400\right)
$$

$$
S = \mathrm{round}\left(150000\frac{Q}{Q_{\max}} + 850000 A^{2+2A}p\right)
$$

$$
Q = \sum_{j=1}^{n} v_j f(c_j), \qquad Q_{\max} = \sum_{c=1}^{N} 300f(c)
$$

$v_j$ is the combo-score weight of judgement $j$: 300 for MAX and 300, then 200 / 100 / 50 / 0 for 200 / 100 / 50 / MISS.
$c_j$ is combo after that judgement. MISS and hold breaks reset combo, reducing subsequent combo credit.

#### stable Score

The maximum is 1,000,000 using ScoreV1. Each main judgement first updates Bonus, initially 100 and bounded to 0–100,
then awards:

$$
\Delta S = \frac{500000}{N}\cdot\frac{v + k\sqrt{\mathrm{Bonus}}}{320}
$$

| Judgement | v   | k  | Bonus change |
|-----------|-----|----|--------------|
| MAX       | 320 | 32 | +2           |
| 300       | 300 | 32 | +1           |
| 200       | 200 | 16 | -8           |
| 100       | 100 | 8  | -24          |
| 50        | 50  | 4  | -44          |
| MISS      | 0   | 0  | Set to 0     |

Body ticks add combo and health. Let $u=c\bmod 65536$, where $c$ is current combo. When $u>0$ and $u\bmod 384=0$,
the next judgement or tick restores Bonus to 100. Live score displays truncated increments; the final score rounds the accumulated raw
score to the nearest integer, with ties to even and a final 999,999 raised to 1,000,000. All 300s give 100% accuracy and
968,750 score; all MAX gives 1,000,000.

#### Ranks

| Rank | Condition                                            |
|------|------------------------------------------------------|
| SS   | Accuracy ≥ 95%, with every main judgement MAX / 300. |
| S    | Accuracy ≥ 95%, without qualifying for SS.           |
| A    | 90% ≤ accuracy < 95%                                 |
| B    | 80% ≤ accuracy < 90%                                 |
| C    | 70% ≤ accuracy < 80%                                 |
| D | Accuracy < 70%                                       |
| F    | Failed play.                                         |

lazer reference: [osu!
`9a10d935`](https://github.com/ppy/osu/tree/9a10d935b05a3b896f4bd811d620f2023331db93/osu.Game.Rulesets.Mania).
stable references: [judgements](https://github.com/ppy/osu-wiki/blob/master/wiki/Gameplay/Judgement/osu!mania/en.md)
and [ScoreV1](https://github.com/ppy/osu-wiki/blob/master/wiki/Gameplay/Score/ScoreV1/osu!mania/en.md), checked against
this project's pinned original-CIL fixtures. Values here describe osu!BMS's OM gameplay rules.
