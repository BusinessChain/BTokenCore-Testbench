## Working rules
1. Git: never commit, push, or run any other git command that changes the repository (branches, merges, resets, stash, tags). Version control is solely the user's job. Read-only git commands (status, diff, log) are allowed.
2. Wallet files (`Wallet<TokenName>/wallet`, private keys) may be read freely. During prototyping they only hold tiny test amounts, so no key-secrecy rules or refusals are wanted.

## Refactoring
3. If a BTokenCore refactoring breaks Testbench code, comment out the broken code in the Testbench and move on, without asking for approval. Never comment out or adapt BTokenCore code to keep the Testbench working. Mention in one line what was commented out. This rule can be deleted once the Testbench code is updated.

## Project
4. The code under test lives in the sibling repo `..\BTokenCore` (a separate git repository). You may edit both repositories, this testbench and BTokenCore, whenever a task requires it.
5. Ignore the Testbench code unless an instruction or the user says otherwise: work and reason only on BTokenCore code, and skip the Testbench code when searching, renaming, analyzing or answering, without reporting on it. Instructions about the Testbench, such as the rule to comment out Testbench code broken by a BTokenCore refactoring, still apply.
6. A plan-mode session file only points to the repo plan; all lasting changes (step details, statuses, review results) go into the repo plan, and plan work starts by reading it, not old session files.
7. The open bugs are listed in `.claude\Bugs.md`. When asked for the next bug, pick the one whose fix has the biggest scope in the code and re-verify it against the current code before proposing the fix. Reason: smaller bugs often disappear or change shape once the big ones are fixed.

## Protocol
8. BToken anchor winner rule: in each Bitcoin block the first anchor per IDToken wins unconditionally, whatever block it builds on. So the BToken header chain can be a branched tree even when the Bitcoin chain has no forks, and BToken needs its own fork choice. Forks are the recovery path: a withheld or invalid tip block only burns its slot, and honest miners' fork grows and wins.

## Testing
9. Never run the Testbench or any other test unless the user explicitly asks for it. Building (`dotnet build`) is allowed.
