# Plan: No-Headerchain-Sync-BToken

## Context
At the moment BToken syncs headers-first from its own peers and loads its own DB in a separate loop (`Blockchain.LoadBlockchain`). This causes several bugs: #42 (header append throws before Bitcoin catches up), #36/#37/#40 (header serving and peer tips). The new model works like this:
- Every winning anchor in an applied Bitcoin block creates a BToken **placeholder**.
- A placeholder is a `HeaderBToken` with `Hash`, `HashPrevious` and `HeaderParent` set. It is appended to the existing `Chain` tree, so forks, `AdvanceTipBlockchain`, `IsStrongerThan` and `Reorg` work unchanged.
- The block behind a placeholder comes from one of three sources: the local DB (at load), the network (sync) or the miner cache (later).
- BToken peers serve only blocks, no headers.

Each step builds. BToken download doesn't work end-to-end today anyway, so a step may leave BToken temporarily broken. While the migration is under way, placeholders and network headers can live in the tree side by side, because `TrySearchHeaderAncestor` dedups by hash.

## Steps

### A. Placeholders
**A1. Create a placeholder for each winning anchor.** ✅ DONE 2026-10-05: builds and was reviewed by the user. The user also split the call in `InsertHeaderPlaceholder` into a local `headerPlaceholder`.

1. `Token/Token.cs`: new virtual, next to `MineBlock` and in the same pattern:
   ```csharp
   public virtual Header CreateHeaderPlaceholder(TXOutputTokenAnchor anchor, Header headerParent)
   { throw new NotSupportedException(); }
   ```
2. `BToken/TokenBToken.cs`: override. It reuses the existing `HeaderBToken` constructor, which also sets `Difficulty`, `BlockRewardInitial` and `PeriodHalveningBlockReward` (needed by `VerifyCoinbase`), so no new constructor is required:
   ```csharp
   public override Header CreateHeaderPlaceholder(TXOutputTokenAnchor anchor, Header headerParent)
   {
     return new HeaderBToken(
       headerHash: anchor.HashBlockReferenced,
       hashPrevious: anchor.HashBlockPreviousReferenced,
       merkleRootHash: new byte[32],
       hashDatabase: new byte[32],
       nonce: 0)
     {
       HeaderParent = headerParent
     };
   }
   ```
3. `Blockchain/Blockchain.cs`: new method
   ```csharp
   internal void InsertHeaderPlaceholder(Block blockParent)
   {
     if (!blockParent.Header.AnchorsWinner.TryGetValue(Token.IDToken, out TXOutputTokenAnchor anchorWinner))
       return;

     Header headerPlaceholder = Token.CreateHeaderPlaceholder(anchorWinner, blockParent.Header);

     ChainRoot.ExtendHeaderchain([headerPlaceholder], out Header headerTipReceivedLast);
   }
   ```
   `ExtendHeaderchain` handles every case:
   - The previous hash is the tip, so the placeholder is appended.
   - The previous hash is deeper in the tree, so a child `Chain` (fork) is created.
   - The previous hash is unknown, so `headerTipReceivedLast` is null and the slot is burned.
   - The hash is already in the tree, so the placeholder is deduped.
4. `Node.cs`: replace `BlockchainBitcoin.OnBlockInserted = Miner.OnBlockBitcoinInserted;` with
   ```csharp
   BlockchainBitcoin.OnBlockInserted += BlockchainBToken.InsertHeaderPlaceholder;
   BlockchainBitcoin.OnBlockInserted += Miner.OnBlockBitcoinInserted;
   ```
   Order: placeholder first, then the miner.

Notes:
- **No locking.** The semaphore is shared, and `InsertBlockReturnNextDownload` already holds it when it fires the event. At load time (`LoadBlockchain`), nothing runs concurrently.
- **Must not throw.** The event fires inside `Blockchain.InsertBlock` after the DB write, so an exception would leave Bitcoin's `HeaderTipBlockchain` out of step. `ExtendHeaderchain` throws only on a `HashPrevious` mismatch, which can't happen here because the ancestor is found by hash.
- **Old anchor search stays for now.** `HeaderBToken.TryAppendToHeader` still searches for the anchor and finds the same Bitcoin header. It is removed in C6.
- **Temporary breakage.** BToken's own `LoadBlockchain` breaks until B3/B4. The first stored header no longer fits onto the placeholder tip, so it throws, the catch fires and the loop stops.
- **Testing.** Mainnet blocks from height 900000 likely contain no BTK anchors, so in practice this is a no-op. Verify by build and Testbench run.

**A2. `Block.Parse` fills the placeholder.** ✅ DONE 2026-10-05: builds, incl. the `HashPrevious` check. Finalized by the user: `CopyContent` renamed to `CopyTo` with reversed direction (`header.CopyTo(Header)`, parsed → tree header); base still copies `MerkleRoot`/`Nonce` (also for Bitcoin, harmless).

Problem: `Block.Parse` keeps the existing `Header` object, because the tree links (`HeaderPrevious`/`HeaderNext`, `Height`, `HeaderParent`) hang on it, and it only compares hashes. A placeholder has no `MerkleRoot`, so the merkle check would fail, and `Header.Serialize()` (DB header write) would write zeros.

Changes:
1. `Token/Header.cs`: new method
   ```csharp
   internal virtual void CopyContent(Header header)
   {
     MerkleRoot = header.MerkleRoot;
     Nonce = header.Nonce;
   }
   ```
2. `BToken/HeaderBToken.cs`: override
   ```csharp
   internal override void CopyContent(Header header)
   {
     base.CopyContent(header);
     HashDatabase = ((HeaderBToken)header).HashDatabase;
   }
   ```
   `HeaderBitcoin` gets no override. Bitcoin headers in the tree always come from headers-first and are already complete.
3. `Token/Block.cs` `Parse()`, replacing the two `if`s after `ParseHeader`:
   ```csharp
   if (Header == null)
     Header = header;
   else if (!Header.Hash.IsAllBytesEqual(header.Hash))
     throw new ProtocolException($"Received unexpected block {header} expected was {Header}.");
   else
     Header.CopyContent(header);
   ```
   The rest of `Parse` is unchanged. The merkle check still runs against `Header`, which is now filled.

Effect today: none. All current callers (`BlockMessage.Run`, `Blockchain.GetBlock`/`LoadBlockchain`/`RollBack`) pass complete headers, and equal hashes mean equal values. `Miner.TryGetBlockMined` passes `Header == null` and stays on the first branch.

Verify: build BToken and the Testbench, then run the Testbench (`MakeAnInstanceOfNode`, `StartNode`).

### B. Startup from Bitcoin
**B1. DB writes become idempotent.** (`Blockchain.InsertBlock`) ✅ DONE 2026-10-05: builds.
- `Insert` → `Upsert` for both the headers and the blocks collection.
- This fixes the duplicate `_id` when loading re-applies stored blocks, and when stale DB rows above the tip get overwritten.
- No load flag is needed.

**B2. Extract the insert core.** (`Blockchain.InsertBlockReturnNextDownload`) ✅ DONE 2026-10-05: builds. Core is `Blockchain.InsertBlocksQueued(Chain chain)`.
- Move the part that runs after queueing into its own method (chain advance, `IsStrongerThan`/`Reorg`, root apply loop).
- Pure refactor, so the DB path can reuse it.

**B3. Placeholder pulls its block from the DB.** (`InsertHeaderPlaceholder` from A1)
1. `Blockchain/Blockchain.cs`: new field `SHA256 SHA256 = SHA256.Create();` (same as in `Block`). `LoadBlockchain` uses it instead of its local `sHA256`.
2. `InsertHeaderPlaceholder` continues after `ExtendHeaderchain`:
   ```csharp
   if (headerTipReceivedLast != headerPlaceholder)
     return;

   BsonDocument bsonDocumentBlock = DatabaseBlockCollection.FindById(headerPlaceholder.Height);

   if (bsonDocumentBlock == null)
     return;

   byte[] blockBytes = bsonDocumentBlock["blockBytes"].AsBinary;
   int startIndex = 0;

   if (!Token.ParseHeader(blockBytes, ref startIndex, SHA256).Hash.IsAllBytesEqual(headerPlaceholder.Hash))
     return;

   Block block = TakeBlockFromPool();
   block.LoadBuffer(blockBytes);
   block.Header = headerPlaceholder;
   block.Parse();

   Chain chain = ChainRoot.FindChain(headerPlaceholder);
   chain.Blocks.Add(headerPlaceholder.Height, block);

   InsertBlocksQueued(chain);
   ```

Why it looks like this:
- `headerTipReceivedLast == headerPlaceholder` holds only when the placeholder was freshly appended. Burned (null) and deduped (an existing tree header, which may already have its block) are skipped. That way `Blocks.Add` can't hit a duplicate key.
- The hash is read from the block bytes themselves, which start with the header. That way there's no second read from the headers collection. A mismatch means DB row `h` belongs to another fork, so the block stays missing and gets downloaded later.
- `TryQueueBlock` isn't used, because the placeholder was never handed out for download and so isn't in `HeadersAwaitingBlock`.
- Root chain at tip+1: applied directly. Fork: `AdvanceTipBlockchain` + `IsStrongerThan` → `Reorg`. The DB holds a single chain, so a reorg during load never rolls back DB blocks.
- `InsertBlock` writes the same rows back. This is harmless thanks to B1.

Notes:
- **No lock**: the shared semaphore is already held by the caller (as in A1).
- **No try/catch**: `Parse`/`Token.InsertBlock` only fail on a corrupt DB, because stored blocks were already validated. If one does throw, the exception escapes from Bitcoin's `InsertBlock`. During Bitcoin's `LoadBlockchain` that ends the load, which is visible and not silent.
- **Until B4**, `BlockchainBToken.LoadBlockchain()` (`Node.cs:68`) still runs after Bitcoin's load. Its first `TryAppendHeader` onto the placeholder tip returns false or throws, and then it stops. That is harmless.

**B4. Remove the BToken load loop.** (`Node.Start`)
- Delete `BlockchainBToken.LoadBlockchain();` (`Node.cs:68`). No reordering is needed: `BlockchainBitcoin.LoadBlockchain()` already runs first (`Node.cs:65`) and now loads BToken as a side effect.
- `Blockchain.LoadBlockchain` stays, because Bitcoin still uses it.

### C. Sync without BToken headers
**C1. Blockchain keeps a reference to its parent `Blockchain`.**
- The constructor already receives it.
- The tip-mode check in C5 needs Bitcoin's `ChainRoot.HeaderTipBlockchain`.

**C2. Download selection without a peer tip.** (`Blockchain.FetchHeaderDownload`, `Chain.FetchHeaderDownload`)
- If `headerTipPeer == null`, walk the whole tree: root first, then the children depth-first.
- The first free header wins, with no height limit.
- This is the catch-up mode, which works because "every peer has it".
- Bitcoin with a known peer tip is unchanged.

**C3. BToken network stops requesting and serving headers.** (`Network/NetworkMessage.cs`, `Network.cs`, `ConfigNetwork.cs`, `Node.cs`)
- New `ConfigNetwork` flag, off for BToken. When it is off:
  - `VerAckMessage` and `InvMessage` send no `getheaders`.
  - `GetHeadersMessage` is not registered in `CreateStateMachineProtocol`, so requests fall through to `UnknownMessage`.

**C4. Announcement cache.** (`HeadersMessage.Run`)
- First fix bug #11: `HeadersMessage.SendHeaders` prefixes the byte count instead of the header count, so an announced header (`Network.AnnounceHeader`) is parsed as dozens of headers and throws. Fix: `VarInt.GetBytes(headersSerialized.Count)`.
- With the flag off, announced headers are parsed and their hashes are stored in a small bounded per-peer set (for example the last 20).
- They no longer go to `TryExtendHeaderchain`.
- This also covers announcements that arrive before the Bitcoin block.

**C5. Tip mode.** (`Network.StartBlockDownloadDispatcher`, `BlockMessage.Run`, `Blockchain.GetHeaderDownload`, `Blockchain.InsertBlockReturnNextDownload`)
- Both call sites pass the peer's announced set along: the dispatcher (via `GetHeaderDownload`) and `BlockMessage.Run` (via `InsertBlockReturnNextDownload`). Both end in `FetchHeaderDownload`.
- A placeholder whose `HeaderParent` is the parent blockchain's `ChainRoot.HeaderTipBlockchain` goes only to a peer that announced it.
- As soon as the next Bitcoin block arrives, the placeholder drops out of tip mode and any peer can serve it. That is the built-in fallback for a missed announcement.

**C6. Remove the anchor search in `HeaderBToken.TryAppendToHeader`.**
- No network headers enter the tree anymore, and `HeaderParent` is set at creation.
- This resolves #42.

**C7. Update `Docs/Bugs.md`.**
- Remove #42.
- Mark #36, #37 and #40 as Bitcoin-only.
- Remove #11 (fixed in C4).
- Mark #39 as relevant to BToken: BToken peers only use `getdata` now, and an unknown hash in `Blockchain.GetBlock` → NRE → the requesting peer is disconnected.

## Out of scope (later)
- Miner cache as the third source: `Miner.InsertBlockMined` is deliberately commented out.
- Bitcoin rollback: placeholders whose `HeaderParent` gets rolled back (#25).

## Verification
After each step:
1. Run `dotnet build` in `..\BTokenCore` and in the Testbench.
2. Comment out any broken Testbench code (rule 4).
3. Run the Testbench only if the user asks for it (rule 7).

End-to-end check after B4 and after C5:
- Start the node with existing `TokenBitcoinNetwork.db` / `TokenBTokenNetwork.db`.
- Confirm that the BToken height after load equals the height before.
- Confirm that no duplicate-`_id` exception occurs.
- Confirm that new BToken blocks are fetched by `getdata` only and that no `getheaders` goes to BToken peers.
