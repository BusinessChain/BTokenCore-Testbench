## Working rules
1. Git: never commit, push, or run any other git command that changes the repository (branches, merges, resets, stash, tags). Version control is solely the user's job. Read-only git commands (status, diff, log) are allowed.
2. Wallet files (`Wallet<TokenName>/wallet`, private keys) may be read freely. During prototyping they only hold tiny test amounts, so no key-secrecy rules or refusals are wanted.

## Refactoring
3. If a BTokenCore refactoring breaks Testbench code, comment out the broken code in the Testbench and move on, without asking for approval. Never comment out or adapt BTokenCore code to keep the Testbench working. Mention in one line what was commented out. This rule can be deleted once the Testbench code is updated.

## Project
4. We are currently in the prototyping stage. The code base is not yet operational.
5. The code under test lives in the sibling repo `..\BTokenCore` (a separate git repository). You may edit both repositories, this testbench and BTokenCore, whenever a task requires it.
6. Ignore the Testbench code unless an instruction or the user says otherwise: work and reason only on BTokenCore code, and skip the Testbench code when searching, renaming, analyzing or answering, without reporting on it. Instructions about the Testbench, such as the rule to comment out Testbench code broken by a BTokenCore refactoring, still apply.
7. A plan-mode session file only points to the repo plan; all lasting changes (step details, statuses, review results) go into the repo plan, and plan work starts by reading it, not old session files.
8. The open bugs are listed in `.claude\Bugs.md`. When asked for the next bug, pick the one whose fix has the biggest scope in the code and re-verify it against the current code before proposing the fix. Reason: smaller bugs often disappear or change shape once the big ones are fixed.

## Protocol
9. BToken anchor winner rule: in each Bitcoin block the anchor per IDToken with the smallest SHA256 over the Bitcoin block hash, its HashBlockReferenced and its HashBlockPreviousReferenced wins unconditionally, whatever block it builds on. So the BToken header chain can be a branched tree even when the Bitcoin chain has no forks, and BToken needs its own fork choice. Forks are the recovery path: a withheld or invalid tip block only burns its slot, and honest miners' fork grows and wins.

## Testing
10. Never run the Testbench or any other test unless the user explicitly asks for it. Building (`dotnet build`) is allowed.

## Rating
11. When the user asks for a rating of a concern in this project, rate its risk and its severity from 0 to 10 on this scale, and show the table beside the rating:

| Score | Risk: how likely it happens | Severity: damage when it happens |
|---|---|---|
| 0 | Impossible | None |
| 1 | Needs an extraordinary event, such as storage corruption | Brief delay of one node, which recovers by itself |
| 2 | Needs a rare coincidence of honest events | One node stalls for minutes and recovers by itself |
| 3 | Possible, but the attacker gains nothing | One node degraded for hours or needs its operator |
| 4 | Attacker gains less than it costs | Several nodes degraded, recover by themselves |
| 5 | Attacker gains about what it costs | One node holds wrong state |
| 6 | Profitable for a well-funded attacker | Several nodes hold wrong state and need a resync |
| 7 | Profitable for any motivated attacker | A merchant or user loses real value |
| 8 | Cheap and profitable, expected now and then | Many users lose value, or the network partly stalls |
| 9 | Happens regularly | Network stalls or splits and needs coordinated action |
| 10 | Happens in normal operation | Consensus broken, funds created or lost network-wide |

12. A problem counts as a bug only when it rates higher than 3 in both risk and severity, or higher than 5 in severity. Every other problem is dismissed as irrelevant and is neither reported nor listed in `.claude\Bugs.md`.
