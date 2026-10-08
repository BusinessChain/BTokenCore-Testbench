# Bugs

Verified open bugs / TODOs in BTokenCore (networking, block download, blockchain, mining), kept so we can fix them later one by one.

Bug/TODO list requested by the user 2026-09-26. Re-verified against the code 2026-10-08 after the header-placeholder plan (BToken gets its tree only from placeholders created from winning anchors in applied Bitcoin blocks; no BToken header requests; "notfound" sent and handled; newest placeholder requested only from announcing peers). Re-check before fixing (user edits in parallel). Remove items once fixed. Every fix needs approval per CLAUDE.md.

Class names as of 2026-10-08: `Blockchain` (chain + persistence, `Blockchain\Blockchain.cs`), `Chain` (nested tree node, `Blockchain\Chain.cs`), `Network` (peers + dispatcher), `Miner` (mining, `Miner.cs`).

**Block download / chain tree**
6. Awaiting fallback in `Chain.FetchHeaderDownloadInChain` (lowest header in `HeadersAwaitingBlock`, skipping only refused ones) hands the same header to every idle peer → duplicate block requests each dispatcher round. (It is also what re-assigns the header of a peer that disconnects mid-download — such headers are NOT orphaned, keep that property when fixing.)
7. `HeadersMessage.Run` (Bitcoin): non-connecting headers are silently ignored instead of triggering getheaders with a locator; the follow-up `GetHeadersMessage.SendGetHeaders` is not awaited.
44. Invalid block on the winning branch after a reorg: the old branch is already rolled back, `Token.InsertBlock` throws at h → root stays at h-1 (possibly = fork) while the valid demoted branch is longer. `IsStrongerThan` only runs for the chain that just received a block, so the demoted chain isn't re-checked until a new block arrives on it.
45. `TokenBToken.RollBack` is a stub: `ReverseSpendInputInDB` / `ReverseOutputInDB` are empty → account DB not rolled back. Rolled-back TXs are also not returned to `TXPool`.
46. Startup double apply: the BToken account DB (`TokenBToken.db`) persists across restarts, but the Bitcoin replay in `LoadBlockchain` re-creates the placeholders, loads the stored BToken blocks and applies them again → balances doubled, spends may fail.
47. BToken root on a pruned Bitcoin stub: BToken lives only on the stretch of Bitcoin blocks that is stored (all headers, only the newest ~200 blocks). Once older Bitcoin blocks are pruned, the oldest placeholder of that stretch has an unknown predecessor → `InsertHeaderPlaceholder` ignores it, and every later placeholder too → BToken tree never grows. The tree needs a root inside the stored stretch. Dormant until pruning exists.
48. Stored BToken block failure escapes into Bitcoin: `InsertHeaderPlaceholder` catches only the `ProtocolException` of `block.Parse`. A matching stored block that fails later (`Token.InsertBlock`, e.g. bad account spend) or throws another exception while parsing propagates into Bitcoin's `InsertBlock` via `OnBlockInserted` → Bitcoin block already stored but tip not advanced (inconsistent chain), later subscribers (`Miner`) skipped; at startup the catch in `LoadBlockchain` stops the Bitcoin load early. A peer block failing the same way only affects that peer.
49. Account staging in `TokenBToken.InsertBlock` (found 2026-10-08): `StageSpendTXInput` reads the sender from the DB (ignoring changes already staged in this block) and adds it with `AccountsStaged.Add` → ArgumentException (not `ProtocolException`) when the sender is already staged, i.e. it sends twice in one block, or received in this block (also from its own TX's outputs, staged first). Valid blocks are rejected / the exception escapes as in #48.

**Messages**
12. `PongMessage.Run` sets `peer.ProtocolStateMachine = null` (Pong isn't registered, so dormant, but wrong).
13. `UnknownMessage.SIZE_BUFFER_PAYLOAD = 4_000_000` probably too big per peer; ~100 KB suggested (user asked about it in a code comment). In BToken, "getheaders" and "inv" now also land here.
15. Sender-side tx throttling: per-peer outgoing byte bucket (~90% of `TXMessage` limit, fee-rate ordered queue) in `Peer.BroadcastTX` / `Miner.MineTokenAnchor`, so we never break our own DoS rule.

**Network**
17. `COUNT_MAX_OUTBOUND_CONNECTIONS = 1` and hard-coded IP in `Network.GetPeer` (used by both the Bitcoin and the BToken network).

**Mining** (`InsertBlockMined` currently commented out in `Miner`)
22. Anchor serialization: `TXOutputTokenAnchor.Serialize()` omits the `OP_RETURN`/length prefix that parsing expects; `LengthDataAnchorToken = 70` but payload is 71 bytes. Also `TXBitcoin.Serialize` iterates `foreach (TXOutputBitcoin output ...)` → InvalidCastException on the anchor output. → own anchors can never be created/recognized.
23. No re-mine when the BToken tip changes: `Miner.OnBlockBitcoinInserted` mines right after the Bitcoin block is applied, before the newest placeholder's block has been announced and downloaded → it mines on the previous BToken tip. Re-mine once the newest block is inserted.
24. Stale anchor TXs: only one open anchor at a time; each re-mine must replace the previous unconfirmed anchor (RBF, same inputs, higher fee), else a stale anchor can win a later slot and waste it.
25. Bitcoin reorg: `Blockchain.RollBack` on Bitcoin notifies nobody; BToken placeholders (and applied blocks) anchored in rolled-back Bitcoin blocks stay in the BToken tree, must be rolled back / removed too.
27. `TokenBToken.CreateTXCoinbase` never adds `tXOutput` to `tX.TXOutputs` (and sets no `Value`) → block reward lost.
28. `TokenBitcoin.TryCreateTXAnchor` doesn't mark spent inputs → next anchor double-spends. Also `OutputsSpendableConfirmed` is never filled (`InsertBlock` fills `OutputsSpendable`) → always returns false.
29. Starting the miner: `IsMining` is never set true; when switched on (future RPC), mine immediately instead of waiting for the next Bitcoin block.
30. Catch-all `catch { return; }` in `Miner.OnBlockBitcoinInserted` hides all errors. Untranslated German comments: `TokenBitcoin.cs` (IndexTXs), `TokenBToken.cs` (TryCreateTXAnchor), `HeaderBToken.cs`.

**Testbench**
31. `StartNode` test's check "each peer's first sent message is `version`" was deleted by the user 2026-09-30 after peers moved into `Network` (private `Peers`). Re-add later; options: make `Network.Peers` internal, or observe sockets via the test `ICommunication`.

**Why:** user wants a persistent list to come back to later.
**How to apply:** when the user asks for "the bug list" or picks the next bug, read this, re-verify the item, propose the fix, wait for approval. Fix order: largest impact first.
