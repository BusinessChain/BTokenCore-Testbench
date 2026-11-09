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
8. The todo list `.claude\Todos.md` holds the open bugs and the improvements the user decided to note for later. When asked for the next bug, pick the one whose fix has the biggest scope in the code and re-verify it against the current code before proposing the fix. Reason: smaller bugs often disappear or change shape once the big ones are fixed.

## Protocol
9. BToken anchor winner rule: in each Bitcoin block the anchor per IDToken with the smallest SHA256 over the Bitcoin block hash, its HashBlockReferenced and its HashBlockPreviousReferenced wins unconditionally, whatever block it builds on. So the BToken header chain can be a branched tree even when the Bitcoin chain has no forks, and BToken needs its own fork choice. Forks are the recovery path: a withheld or invalid tip block only burns its slot, and honest miners' fork grows and wins.

## Testing
10. Never run the Testbench or any other test unless the user explicitly asks for it. Building (`dotnet build`) is allowed.

## Rating
11. When the user asks for a rating of a concern in this project, rate its risk, its severity and its fix effort from 0 to 10 on this scale:

| Score | Risk: how likely it happens | Severity: damage when it happens | Fix effort: decisions the fix needs |
|---|---|---|---|
| 0 | Impossible | None | Code can be deleted |
| 1 | Needs an extraordinary event, such as storage corruption | Brief delay of one node, which recovers by itself | Nothing has to be done |
| 2 | Needs a rare coincidence of honest events | One node stalls for minutes and recovers by itself | 1 decision |
| 3 | Possible, but the attacker gains nothing | One node degraded for hours or needs its operator | 2 decisions |
| 4 | Attacker gains less than it costs | Several nodes degraded, recover by themselves | 3 decisions |
| 5 | Attacker gains about what it costs | One node holds wrong state | Up to 5 decisions |
| 6 | Profitable for a well-funded attacker | Several nodes hold wrong state and need a resync | Up to 8 decisions |
| 7 | Profitable for any motivated attacker | A merchant or user loses real value | Up to 12 decisions |
| 8 | Cheap and profitable, expected now and then | Many users lose value, or the network partly stalls | Up to 20 decisions |
| 9 | Happens regularly | Network stalls or splits and needs coordinated action | Up to 30 decisions |
| 10 | Happens in normal operation | Consensus broken, funds created or lost network-wide | More than 30 decisions, a redesign |

Fix effort counts only decisions. Mechanical edits that follow from a decision, such as moving code or adapting callers, count as none.

12. A problem counts as a bug only when it rates higher than 3 in both risk and severity, or higher than 5 in severity. A problem that is no bug but rates at least 1 in both risk and severity and at most 2 in fix effort is a cheap fix: report it as such so the user can decide to fix it, but don't list it in the todo list unless the user asks. Every other problem is dismissed as irrelevant and is neither reported nor listed.
