using System.Security.Cryptography;

using BTokenCore;

using LiteDB;


namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestBlockchainBitcoin : Test_Testbench
  {
    const int INDEX_NONCE = 76;
    const uint N_BITS_EASY = 0x207FFFFF;
    const byte BYTE_HASH_MOST_SIGNIFICANT_BELOW_TARGET_EASY = 0x7F;
    const string HASH_HEADER_ROOT = "000000000000000000001e39df127cbab82a824ac43cfcdbf62e59f6a08b9f0b";
    const uint UNIX_TIME_SECONDS_ROOT = 1791011796;
    const uint N_BITS_ROOT = 0x17021EF0;
    const uint TIMESPAN_ROOT_TO_HEADER_BEFORE_RETARGET_SECONDS = 971711 - 969696;
    const uint TIMESPAN_RETARGET_SECONDS = 14 * 24 * 60 * 60;
    const uint N_BITS_TARGET_QUADRUPLED = 0x17087BC0;
    const uint N_BITS_TARGET_QUARTERED = 0x170087BC;
    const long VALUE_OUTPUT = 50000;
    const int HEIGHT_ROOT = 969696;
    const int HEIGHT_ABSENT = -1;
    const long VALUE_OUTPUT_WALLET = 1000000;
    const long FEE_ANCHOR = 10000;

    internal TestBlockchainBitcoin(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return Test_ProofOfWork(ref message)
        && Test_TimestampPremature(ref message)
        && Test_MedianTimePast(ref message)
        && Test_Retargeting(ref message)
        && Test_AnchorsBitcoin(out Block blockParsed, ref message)
        && Test_AnchorsStored(blockParsed, ref message)
        && Test_WalletReorg(ref message);
    }

    // Counter-tested 2026-11-14 03:01
    bool Test_ProofOfWork(ref string message)
    {
      Console.WriteLine("  1. Proof Of Work: Reject mainnet header with altered nonce.");

      byte[] headerRaw = Convert.FromHexString(HEADER_BITCOIN_737857);

      uint nonce = BitConverter.ToUInt32(headerRaw, INDEX_NONCE);
      BitConverter.GetBytes(nonce + 1).CopyTo(headerRaw, INDEX_NONCE);

      return IsRejected("Proof of work", () => ParseHeader(headerRaw), ref message);
    }

    // Counter-tested 2026-11-14 03:01
    bool Test_TimestampPremature(ref string message)
    {
      Console.WriteLine("  2. Timestamp Premature: Reject header timestamp over two hours ahead.");

      DateTimeOffset now = DateTimeOffset.UtcNow;

      byte[] headerRawValid = MineHeader((uint)now.AddHours(1).ToUnixTimeSeconds(), new byte[32], new byte[32], h => true).Serialize();
      byte[] headerRawPremature = MineHeader((uint)now.AddHours(3).ToUnixTimeSeconds(), new byte[32], new byte[32], h => true).Serialize();

      return IsAccepted("Timestamp one hour ahead", () => ParseHeader(headerRawValid), ref message)
        && IsRejected("Timestamp premature", () => ParseHeader(headerRawPremature), ref message);
    }

    // Counter-tested 2026-11-14 04:59
    bool Test_MedianTimePast(ref string message)
    {
      Console.WriteLine("  3. Median Time Past: Reject header timestamp below median of last eleven.");

      return IsMedianTimePastEnforced("Headers with a timestamp out of order, the twelfth last above the median", [1, 2, 100, 4, 5, 6, 7, 8, 9, 10, 11, 7, 13, 14], 8, ref message)
        && IsMedianTimePastEnforced("Headers with the last above and the twelfth last below the median", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 100], 9, ref message);
    }

    static bool IsMedianTimePastEnforced(string nameCase, uint[] secondsAfterRoot, uint secondsAfterRootMedian, ref string message)
    {
      BlockchainBitcoin blockchain = CreateBlockchainBitcoin();
      byte[] hashPrevious = HASH_HEADER_ROOT.ToBinary();
      uint unixTimeSecondsMedian = UNIX_TIME_SECONDS_ROOT + secondsAfterRootMedian;

      return IsAccepted(nameCase, () =>
        {
          foreach (uint seconds in secondsAfterRoot)
            hashPrevious = blockchain.InsertHeadersInTree([CreateHeader(hashPrevious, UNIX_TIME_SECONDS_ROOT + seconds, N_BITS_ROOT)]).Hash;
        }, ref message)
        && IsRejected($"{nameCase}, timestamp below median time past", () => blockchain.InsertHeadersInTree([CreateHeader(hashPrevious, unixTimeSecondsMedian - 1, N_BITS_ROOT)]), ref message)
        && IsAccepted($"{nameCase}, timestamp equal to median time past", () => blockchain.InsertHeadersInTree([CreateHeader(hashPrevious, unixTimeSecondsMedian, N_BITS_ROOT)]), ref message);
    }

    // Counter-tested 2026-11-14 04:59
    bool Test_Retargeting(ref string message)
    {
      Console.WriteLine("  4. Retargeting: Keep NBits between retargets, clamp the retarget timespan.");

      BlockchainBitcoin blockchain = CreateBlockchainBitcoin();
      List<Header> headersBase = CreateHeadersInterval(HASH_HEADER_ROOT.ToBinary(), UNIX_TIME_SECONDS_ROOT + 1);
      byte[] hashPrevious = headersBase[^1].Hash;

      blockchain.InsertHeadersInTree(headersBase);

      HeaderBitcoin headerBeforeRetargetFirst = CreateHeader(hashPrevious, UNIX_TIME_SECONDS_ROOT + TIMESPAN_RETARGET_SECONDS, N_BITS_ROOT);
      uint unixTimeSecondsIntervalSecond = UNIX_TIME_SECONDS_ROOT + TIMESPAN_RETARGET_SECONDS * 3 / 2;
      Header headerIntervalSecondFirst = CreateHeader(headerBeforeRetargetFirst.Hash, unixTimeSecondsIntervalSecond, N_BITS_ROOT);
      List<Header> headersIntervalSecond = [headerBeforeRetargetFirst, headerIntervalSecondFirst, .. CreateHeadersInterval(headerIntervalSecondFirst.Hash, unixTimeSecondsIntervalSecond + 1)];
      List<Header> headersIntervalBackdated = CreateHeadersInterval(headerIntervalSecondFirst.Hash, unixTimeSecondsIntervalSecond - TIMESPAN_RETARGET_SECONDS - TIMESPAN_ROOT_TO_HEADER_BEFORE_RETARGET_SECONDS);

      return IsRejected("NBits changed between retargets", () => blockchain.InsertHeadersInTree([CreateHeader(hashPrevious, UNIX_TIME_SECONDS_ROOT + TIMESPAN_ROOT_TO_HEADER_BEFORE_RETARGET_SECONDS, N_BITS_ROOT + 1)]), ref message)
        && IsRetargetCorrect(blockchain, hashPrevious, UNIX_TIME_SECONDS_ROOT, TIMESPAN_RETARGET_SECONDS, N_BITS_ROOT, N_BITS_TARGET_QUADRUPLED, ref message)
        && IsRetargetCorrect(blockchain, hashPrevious, UNIX_TIME_SECONDS_ROOT, 8 * TIMESPAN_RETARGET_SECONDS, N_BITS_TARGET_QUADRUPLED, N_BITS_ROOT, ref message)
        && IsRetargetCorrect(blockchain, hashPrevious, UNIX_TIME_SECONDS_ROOT, TIMESPAN_ROOT_TO_HEADER_BEFORE_RETARGET_SECONDS, N_BITS_TARGET_QUARTERED, N_BITS_ROOT, ref message)
        && IsAccepted("Headers of the second retarget interval", () => blockchain.InsertHeadersInTree(headersIntervalSecond), ref message)
        && IsRetargetCorrect(blockchain, headersIntervalSecond[^1].Hash, unixTimeSecondsIntervalSecond, TIMESPAN_RETARGET_SECONDS, N_BITS_ROOT, N_BITS_TARGET_QUADRUPLED, ref message)
        && IsAccepted("Headers of a retarget interval older than its first header", () => blockchain.InsertHeadersInTree(headersIntervalBackdated), ref message)
        && IsRetargetCorrect(blockchain, headersIntervalBackdated[^1].Hash, unixTimeSecondsIntervalSecond, -TIMESPAN_RETARGET_SECONDS, N_BITS_TARGET_QUARTERED, N_BITS_ROOT, ref message);
    }

    static List<Header> CreateHeadersInterval(byte[] hashPrevious, uint unixTimeSecondsFirst)
    {
      List<Header> headers = [];

      for (uint i = 0; i < TIMESPAN_ROOT_TO_HEADER_BEFORE_RETARGET_SECONDS - 1; i++)
      {
        headers.Add(CreateHeader(hashPrevious, unixTimeSecondsFirst + i, N_BITS_ROOT));
        hashPrevious = headers[^1].Hash;
      }

      return headers;
    }

    static bool IsRetargetCorrect(BlockchainBitcoin blockchain, byte[] hashPrevious, uint unixTimeSecondsIntervalStart, long timespan, uint nBitsExpected, uint nBitsWrong, ref string message)
    {
      uint unixTimeSeconds = (uint)(unixTimeSecondsIntervalStart + timespan);
      Header headerBeforeRetarget = CreateHeader(hashPrevious, unixTimeSeconds, N_BITS_ROOT);

      return IsRejected($"Retarget with timespan {timespan} and NBits {nBitsWrong:X8}", () => blockchain.InsertHeadersInTree([headerBeforeRetarget, CreateHeader(headerBeforeRetarget.Hash, unixTimeSeconds, nBitsWrong)]), ref message)
        && IsAccepted($"Retarget with timespan {timespan} and NBits {nBitsExpected:X8}", () => blockchain.InsertHeadersInTree([CreateHeader(hashPrevious, unixTimeSeconds, N_BITS_ROOT), CreateHeader(headerBeforeRetarget.Hash, unixTimeSeconds, nBitsExpected)]), ref message);
    }

    class BlockchainBitcoinStore : BlockchainBitcoin
    {
      internal BlockchainBitcoinStore()
        : base(CreateLiteDatabase(), new SemaphoreSlim(1))
      { }

      internal Block ReloadBlockStored(Block block)
      {
        Block blockReloaded = new(SerializeBlockStored(block));
        Header header = new HeaderBitcoin(block.Header.Hash, 0, block.Header.HashPrevious, null, 0, 0, 0) { Height = block.Header.Height };

        return TryParseBlockStored(header, blockReloaded) ? blockReloaded : null;
      }
    }

    class BlockchainBitcoinEasy : BlockchainBitcoinStore
    {
      protected override Header CreateHeaderRoot()
      {
        return CreateHeaderRootEasy();
      }

      internal int GetHeightSpentOutputOwned(byte[] tXID, int index)
      {
        if (OutputsOwnedStored.FindById(TXOutputOwned.CreateID(tXID, index)) is not TXOutputOwned output)
          return HEIGHT_ABSENT;

        return output.HeightSpent;
      }

      internal int GetHeightConfirmedTXOwned(byte[] hash)
      {
        if (TXsOwnedStored.FindById(hash) is not BsonDocument documentTXOwned)
          return HEIGHT_ABSENT;

        return documentTXOwned[NAME_FIELD_HEIGHT_CONFIRMED].AsInt32;
      }
    }

    static HeaderBitcoin CreateHeaderRootEasy()
    {
      HeaderBitcoin header = CreateHeader(new byte[32], UNIX_TIME_SECONDS_ROOT, N_BITS_EASY);
      header.Height = HEIGHT_ROOT;

      return header;
    }

    static BlockchainBitcoinStore CreateBlockchainBitcoin()
    {
      CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);

      return new BlockchainBitcoinStore();
    }

    static HeaderBitcoin CreateHeader(byte[] hashPrevious, uint unixTimeSeconds, uint nBits)
    {
      HeaderBitcoin header = new(
        headerHash: null,
        version: 0x20000000,
        hashPrevious: hashPrevious,
        merkleRootHash: new byte[32],
        unixTimeSeconds: unixTimeSeconds,
        nBits: nBits,
        nonce: 0);

      header.ComputeHash();

      return header;
    }

    // Counter-tested 2026-11-14 03:35
    bool Test_AnchorsBitcoin(out Block blockParsed, ref string message)
    {
      Console.WriteLine("  5. Bitcoin Anchors: Draw anchor winners from TXBitcoin outputs of twenty Bitcoin blocks.");

      const int HEIGHT_BLOCK = 800000;
      byte[] iDTokenSecond = [0x05, 0x06, 0x07, 0x08];

      TXOutputTokenAnchor[] anchorsFirst = [
        CreateTXOutputTokenAnchor(ID_TOKEN, 1),
        CreateTXOutputTokenAnchor(ID_TOKEN, 2),
        CreateTXOutputTokenAnchor(ID_TOKEN, 3)];

      TXOutputTokenAnchor[] anchorsSecond = [
        CreateTXOutputTokenAnchor(iDTokenSecond, 4),
        CreateTXOutputTokenAnchor(iDTokenSecond, 5)];

      TXOutputTokenAnchor anchorIneligible = CreateTXOutputTokenAnchor(ID_TOKEN, 6);

      Block block = new(SIZE_BLOCK_MAX);
      block.TXs.AddRange([
        CreateTXBitcoin(0, CreateTXOutputP2PKH()),
        CreateTXBitcoin(HEIGHT_BLOCK - 1, CreateTXOutputP2PKH(), anchorsFirst[0], anchorsSecond[0]),
        CreateTXBitcoin(HEIGHT_BLOCK - 1, anchorsFirst[1], CreateTXOutputP2PKH()),
        CreateTXBitcoin(HEIGHT_BLOCK - 1, anchorsFirst[2], anchorsSecond[1]),
        CreateTXBitcoin(HEIGHT_BLOCK, anchorIneligible, CreateTXOutputP2PKH())]);

      const int COUNT_BLOCKS_DRAWN = 20;
      blockParsed = null;

      for (uint i = 0; i < COUNT_BLOCKS_DRAWN; i++)
      {
        block.Header = MineHeader(
          UNIX_TIME_SECONDS_ROOT + i,
          new byte[32],
          block.ComputeMerkleRoot(),
          h => string.CompareOrdinal(ComputeScoreAnchor(h, anchorIneligible), ComputeScoreAnchor(h, GetAnchorScoreLowest(h, anchorsFirst))) < 0);

        block.Header.Height = HEIGHT_BLOCK;
        block.Serialize();

        blockParsed = new(block.Buffer[..block.LengthDataPayload])
        {
          Header = block.Header
        };

        new ParserBitcoin().ParseBlock(blockParsed, verifySignatures: false);

        string nameParser = $"Anchor winners drawn by ParserBitcoin in block {block.Header.Hash.ToHexString()}";

        if (!IsFieldEqual(nameParser, "AnchorsWinner.Count", 2, blockParsed.Header.AnchorsWinner.Count, ref message)
          || !IsAnchorWinnerLowestScore(nameParser, blockParsed.Header, anchorsFirst, ref message)
          || !IsAnchorWinnerLowestScore(nameParser, blockParsed.Header, anchorsSecond, ref message))
          return false;
      }

      return true;
    }

    // Counter-tested 2026-11-14 03:35
    bool Test_AnchorsStored(Block blockParsed, ref string message)
    {
      Console.WriteLine("  6. Stored Anchors: Keep anchor winners through SerializeBlockStored and ParseBlockStored.");

      const string NAME_CASE = "Anchor winners reloaded by ParseBlockStored";

      if (CreateBlockchainBitcoin().ReloadBlockStored(blockParsed) is not Block blockReloaded)
      {
        message = $"{NAME_CASE}: the stored block cannot be parsed.";
        return false;
      }

      if (!IsFieldEqual(NAME_CASE, "AnchorsWinner.Count", blockParsed.Header.AnchorsWinner.Count, blockReloaded.Header.AnchorsWinner.Count, ref message))
        return false;

      foreach (TXOutputTokenAnchor anchor in blockParsed.Header.AnchorsWinner.Values)
      {
        string iDToken = anchor.IDToken.ToHexString();

        if (!blockReloaded.Header.AnchorsWinner.TryGetValue(anchor.IDToken, out TXOutputTokenAnchor anchorReloaded))
        {
          message = $"{NAME_CASE}: no anchor winner of IDToken {iDToken}.";
          return false;
        }

        if (!IsFieldEqual(NAME_CASE, $"HashBlockReferenced of IDToken {iDToken}", anchor.HashBlockReferenced, anchorReloaded.HashBlockReferenced, ref message)
          || !IsFieldEqual(NAME_CASE, $"HashBlockPreviousReferenced of IDToken {iDToken}", anchor.HashBlockPreviousReferenced, anchorReloaded.HashBlockPreviousReferenced, ref message))
          return false;
      }

      return true;
    }

    TXOutputP2PKH CreateTXOutputP2PKH()
    {
      return new TXOutputP2PKH(VALUE_OUTPUT, Testbench.Wallet.Hash160KeyPublicReceiving);
    }

    BlockchainBitcoin.TXBitcoin CreateTXBitcoin(int lockTime, params TXOutput[] tXOutputs)
    {
      BlockchainBitcoin.TXBitcoin tX = new() { LockTime = lockTime };

      tX.Inputs.Add(new BlockchainBitcoin.TXInputBitcoin
      {
        TXIDOutput = RandomNumberGenerator.GetBytes(32),
        IndexOutput = 0
      });

      tX.TXOutputs.AddRange(tXOutputs);
      tX.Serialize(Testbench.Wallet, Testbench.Wallet.Hash160KeyPublicReceiving);

      return tX;
    }

    // Counter-tested 2026-11-14 03:01
    bool Test_WalletReorg(ref string message)
    {
      Console.WriteLine("  7. Wallet Reorg: Roll back wallet outputs and own TXs, restore on reversal.");

      CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
      BlockchainBitcoinEasy blockchain = new();

      BlockchainBitcoin.TXBitcoin tXPayingWallet = CreateTXBitcoin(0, new TXOutputP2PKH(VALUE_OUTPUT_WALLET, blockchain.Wallet.Hash160KeyPublicReceiving));

      Block blockFirst = CreateBlockBitcoin(CreateHeaderRootEasy().Hash, 1, CreateTXBitcoin(0, CreateTXOutputP2PKH()));
      Block blockPaying = CreateBlockBitcoin(blockFirst.Header.Hash, 2, CreateTXBitcoin(0, CreateTXOutputP2PKH()), tXPayingWallet);

      if (!TryDeliverBlocks(blockchain, [blockFirst, blockPaying], ref message)
        || !TryCreateAnchorOwned(blockchain, out TX tXAnchorSpending, ref message))
        return false;

      Block blockSpending = CreateBlockBitcoin(blockPaying.Header.Hash, 3, CreateTXBitcoin(0, CreateTXOutputP2PKH()), tXAnchorSpending);

      if (!TryDeliverBlocks(blockchain, [blockSpending], ref message)
        || !TryCreateAnchorOwned(blockchain, out TX tXAnchorSpendingChange, ref message))
        return false;

      int heightSpending = HEIGHT_ROOT + 3;
      const string NAME_CASE_SPENT = "Wallet after the block spending the wallet output";

      if (!IsFieldEqual(NAME_CASE_SPENT, "HeightSpent of the paid output", heightSpending, blockchain.GetHeightSpentOutputOwned(tXPayingWallet.Hash, 0), ref message)
        || !IsFieldEqual(NAME_CASE_SPENT, "HeightSpent of the change output", 0, blockchain.GetHeightSpentOutputOwned(tXAnchorSpending.Hash, 1), ref message)
        || !IsFieldEqual(NAME_CASE_SPENT, "heightConfirmed of the spending anchor", heightSpending, blockchain.GetHeightConfirmedTXOwned(tXAnchorSpending.Hash), ref message)
        || !IsFieldEqual(NAME_CASE_SPENT, "heightConfirmed of the anchor spending the change", 0, blockchain.GetHeightConfirmedTXOwned(tXAnchorSpendingChange.Hash), ref message))
        return false;

      if (!TryDeliverBlocks(blockchain, CreateBlocksEmpty(blockPaying.Header.Hash, 3, 4), ref message))
        return false;

      const string NAME_CASE_ROLLED_BACK = "Wallet after the reorg below the spending block";

      if (!IsFieldEqual(NAME_CASE_ROLLED_BACK, "height of the Bitcoin tip", HEIGHT_ROOT + 4, blockchain.GetHeight(), ref message)
        || !IsFieldEqual(NAME_CASE_ROLLED_BACK, "HeightSpent of the paid output", 0, blockchain.GetHeightSpentOutputOwned(tXPayingWallet.Hash, 0), ref message)
        || !IsFieldEqual(NAME_CASE_ROLLED_BACK, "HeightSpent of the change output", HEIGHT_ABSENT, blockchain.GetHeightSpentOutputOwned(tXAnchorSpending.Hash, 1), ref message)
        || !IsFieldEqual(NAME_CASE_ROLLED_BACK, "heightConfirmed of the spending anchor", 0, blockchain.GetHeightConfirmedTXOwned(tXAnchorSpending.Hash), ref message)
        || !IsFieldEqual(NAME_CASE_ROLLED_BACK, "heightConfirmed of the anchor spending the change", HEIGHT_ABSENT, blockchain.GetHeightConfirmedTXOwned(tXAnchorSpendingChange.Hash), ref message))
        return false;

      if (!TryDeliverBlocks(blockchain, CreateBlocksEmpty(blockFirst.Header.Hash, 2, 5), ref message))
        return false;

      const string NAME_CASE_UNPAID = "Wallet after the reorg below the paying block";

      if (!IsFieldEqual(NAME_CASE_UNPAID, "height of the Bitcoin tip", HEIGHT_ROOT + 5, blockchain.GetHeight(), ref message)
        || !IsFieldEqual(NAME_CASE_UNPAID, "HeightSpent of the paid output", HEIGHT_ABSENT, blockchain.GetHeightSpentOutputOwned(tXPayingWallet.Hash, 0), ref message)
        || !IsFieldEqual(NAME_CASE_UNPAID, "heightConfirmed of the spending anchor", HEIGHT_ABSENT, blockchain.GetHeightConfirmedTXOwned(tXAnchorSpending.Hash), ref message))
        return false;

      if (!TryDeliverBlocks(blockchain, [blockPaying, blockSpending, .. CreateBlocksEmpty(blockSpending.Header.Hash, 4, 6)], ref message))
        return false;

      const string NAME_CASE_RESTORED = "Wallet after the reversed reorg";

      return IsFieldEqual(NAME_CASE_RESTORED, "height of the Bitcoin tip", HEIGHT_ROOT + 6, blockchain.GetHeight(), ref message)
        && IsFieldEqual(NAME_CASE_RESTORED, "HeightSpent of the paid output", heightSpending, blockchain.GetHeightSpentOutputOwned(tXPayingWallet.Hash, 0), ref message)
        && IsFieldEqual(NAME_CASE_RESTORED, "HeightSpent of the change output", 0, blockchain.GetHeightSpentOutputOwned(tXAnchorSpending.Hash, 1), ref message)
        && IsFieldEqual(NAME_CASE_RESTORED, "heightConfirmed of the spending anchor", HEIGHT_ABSENT, blockchain.GetHeightConfirmedTXOwned(tXAnchorSpending.Hash), ref message);
    }

    List<Block> CreateBlocksEmpty(byte[] hashPrevious, uint secondsAfterRootFirst, uint secondsAfterRootLast)
    {
      List<Block> blocks = [];

      for (uint secondsAfterRoot = secondsAfterRootFirst; secondsAfterRoot <= secondsAfterRootLast; secondsAfterRoot++)
      {
        blocks.Add(CreateBlockBitcoin(hashPrevious, secondsAfterRoot, CreateTXBitcoin(0, CreateTXOutputP2PKH())));
        hashPrevious = blocks[^1].Header.Hash;
      }

      return blocks;
    }

    static bool TryCreateAnchorOwned(BlockchainBitcoin blockchain, out TX tXAnchor, ref string message)
    {
      tXAnchor = null;

      if (!blockchain.TryCreateTXAnchor(CreateTXOutputTokenAnchor(ID_TOKEN, 1), FEE_ANCHOR, out _))
      {
        message = "Own anchor: TryCreateTXAnchor found no wallet output to spend.";
        return false;
      }

      List<byte[]> hashesTXOwned = blockchain.GetHashesTXOwnedUnconfirmed();

      if (hashesTXOwned.Count != 1)
      {
        message = $"Own anchor: {hashesTXOwned.Count} unconfirmed own TXs at the Bitcoin tip, expected 1.";
        return false;
      }

      tXAnchor = blockchain.GetTX(hashesTXOwned[0]);

      return true;
    }

    static Block CreateBlockBitcoin(byte[] hashPrevious, uint secondsAfterRoot, params TX[] tXs)
    {
      Block block = new(SIZE_BLOCK_MAX);
      block.TXs.AddRange(tXs);

      block.Header = MineHeader(UNIX_TIME_SECONDS_ROOT + secondsAfterRoot, hashPrevious, block.ComputeMerkleRoot(), h => true);
      block.Serialize();

      return block;
    }

    static bool TryDeliverBlocks(BlockchainBitcoin blockchain, List<Block> blocks, ref string message)
    {
      blockchain.InsertHeadersInTree(blocks.Select(b => b.Header).ToList());

      foreach (Block block in blocks)
      {
        Header headerRequested = blockchain.FetchHeaderBlockMissingNext(h => true);

        if (headerRequested == null || !headerRequested.Hash.IsAllBytesEqual(block.Header.Hash))
        {
          message = $"Block delivery: block {block.Header.Hash.ToHexString()} was not requested next.";
          return false;
        }

        blockchain.ReceiveBlock(block);
      }

      return true;
    }

    static HeaderBitcoin MineHeader(uint unixTimeSeconds, byte[] hashPrevious, byte[] merkleRoot, Func<byte[], bool> isHashWanted)
    {
      HeaderBitcoin header = CreateHeader(hashPrevious, unixTimeSeconds, N_BITS_EASY);
      header.MerkleRoot = merkleRoot;

      do
      {
        header.Nonce += 1;
        header.ComputeHash();
      }
      while (header.Hash[^1] >= BYTE_HASH_MOST_SIGNIFICANT_BELOW_TARGET_EASY || !isHashWanted(header.Hash));

      return header;
    }

    static void ParseHeader(byte[] headerRaw)
    {
      int index = 0;
      new ParserBitcoin().ParseHeader(headerRaw, ref index);
    }
  }
}
