## Working rules
1. Git: never commit, push, or run any other git command that changes the repository (branches, merges, resets, stash, tags). Version control is solely the user's job. Read-only git commands (status, diff, log) are allowed.
2. Wallet files (`Wallet<TokenName>/wallet`, private keys) may be read freely. During prototyping they only hold tiny test amounts, so no key-secrecy rules or refusals are wanted.

## Refactoring
3. Before fixing a broken mechanism, question whether it should exist at all or whether its underlying assumption is wrong. Prefer removing or redefining it over building a fix on top of it.
4. If a BTokenCore refactoring breaks Testbench code, comment out the broken code in the Testbench and move on, without asking for approval. Never comment out or adapt BTokenCore code to keep the Testbench working. Mention in one line what was commented out. This rule can be deleted once the Testbench code is updated.

## Project
5. The code under test lives in the sibling repo `..\BTokenCore` (a separate git repository). You may edit both repositories, this testbench and BTokenCore, whenever a task requires it.

## Protocol
6. BToken anchor winner rule: in each Bitcoin block the first anchor per IDToken wins unconditionally, whatever block it builds on. So the BToken header chain can be a branched tree even when the Bitcoin chain has no forks, and BToken needs its own fork choice. Forks are the recovery path: a withheld or invalid tip block only burns its slot, and honest miners' fork grows and wins.
