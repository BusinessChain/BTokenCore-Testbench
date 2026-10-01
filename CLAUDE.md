- If I tell you to delete everything or a large portion of something, or give you other profound instructions that may have serious repercussions, always ask me at least twice before you do it.
- The code under test lives in the sibling repo `..\BTokenCore` (a separate git repository). You may edit both repositories, this testbench and BTokenCore, whenever a task requires it.
- Project: BToken is a blockchain with its own token (BToken), like Bitcoin, but it replaces proof-of-work with proof-of-transaction: a miner declares the hash of the next BToken block by putting an anchor transaction (`TXOutputTokenAnchor`) into a block of the parent chain, Bitcoin. If several anchors are in the same Bitcoin block, the first one in the list wins. Status: prototype. Bold refactorings are welcome, and compatibility with existing data is not a concern.
- Language: English for chat and for code comments.
- Git: never commit, push, or run any other git command that changes the repository (branches, merges, resets, stash, tags). Version control is solely the user's job. Read-only git commands (status, diff, log) are allowed.
- Before fixing a broken mechanism, question whether it should exist at all or whether its underlying assumption is wrong. Prefer removing or redefining it over building a fix on top of it.

Consensus In BToken (proof-of-transaction):
A BToken block becomes eligible only when its hash is anchored in a Bitcoin block by a TXOutputTokenAnchor carrying the BToken IDToken. Per Bitcoin block, the first anchor for that IDToken wins the slot. If the winning anchor references a block that is invalid or never published or if there is no anchor at all, BToken simply misses that slot and honest miners keep mining on the current BToken tip.
