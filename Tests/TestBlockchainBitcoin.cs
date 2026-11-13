using BTokenCore;


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
    const int COUNT_HEADERS_MEDIAN_TIME_PAST = 11;
    const uint TIMESPAN_ROOT_TO_HEADER_BEFORE_RETARGET_SECONDS = 971711 - 969696;
    const uint TIMESPAN_RETARGET_SECONDS = 14 * 24 * 60 * 60;
    const uint N_BITS_TARGET_QUADRUPLED = 0x17087BC0;
    const uint N_BITS_TARGET_QUARTERED = 0x170087BC;

    internal TestBlockchainBitcoin(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return Test_ProofOfWork(ref message)
        && Test_TimestampPremature(ref message)
        && Test_MedianTimePast(ref message)
        && Test_Retargeting(ref message);
    }

    bool Test_ProofOfWork(ref string message)
    {
      Console.WriteLine("  1. Proof Of Work: Reject mainnet header with altered nonce.");

      byte[] headerRaw = Convert.FromHexString(HEADER_BITCOIN_737857);

      uint nonce = BitConverter.ToUInt32(headerRaw, INDEX_NONCE);
      BitConverter.GetBytes(nonce + 1).CopyTo(headerRaw, INDEX_NONCE);

      return IsRejected("Proof of work", () => ParseHeader(headerRaw), ref message);
    }

    bool Test_TimestampPremature(ref string message)
    {
      Console.WriteLine("  2. Timestamp Premature: Reject header timestamp over two hours ahead.");

      DateTimeOffset now = DateTimeOffset.UtcNow;

      byte[] headerRawValid = MineHeader((uint)now.AddHours(1).ToUnixTimeSeconds());
      byte[] headerRawPremature = MineHeader((uint)now.AddHours(3).ToUnixTimeSeconds());

      return IsAccepted("Timestamp one hour ahead", () => ParseHeader(headerRawValid), ref message)
        && IsRejected("Timestamp premature", () => ParseHeader(headerRawPremature), ref message);
    }

    bool Test_MedianTimePast(ref string message)
    {
      Console.WriteLine("  3. Median Time Past: Reject header timestamp below median of last eleven.");

      BlockchainBitcoin blockchain = CreateBlockchainBitcoin();
      byte[] hashPrevious = HASH_HEADER_ROOT.ToBinary();

      for (uint i = 1; i < COUNT_HEADERS_MEDIAN_TIME_PAST; i++)
        hashPrevious = blockchain.InsertHeadersInTree([CreateHeader(hashPrevious, UNIX_TIME_SECONDS_ROOT + i, N_BITS_ROOT)]).Hash;

      uint unixTimeSecondsMedian = UNIX_TIME_SECONDS_ROOT + COUNT_HEADERS_MEDIAN_TIME_PAST / 2;

      return IsRejected("Timestamp below median time past", () => blockchain.InsertHeadersInTree([CreateHeader(hashPrevious, unixTimeSecondsMedian - 1, N_BITS_ROOT)]), ref message)
        && IsAccepted("Timestamp equal to median time past", () => blockchain.InsertHeadersInTree([CreateHeader(hashPrevious, unixTimeSecondsMedian, N_BITS_ROOT)]), ref message);
    }

    bool Test_Retargeting(ref string message)
    {
      Console.WriteLine("  4. Retargeting: Keep NBits between retargets, clamp the retarget timespan.");

      BlockchainBitcoin blockchain = CreateBlockchainBitcoin();
      List<Header> headersBase = [];
      byte[] hashPrevious = HASH_HEADER_ROOT.ToBinary();

      for (uint i = 1; i < TIMESPAN_ROOT_TO_HEADER_BEFORE_RETARGET_SECONDS; i++)
      {
        headersBase.Add(CreateHeader(hashPrevious, UNIX_TIME_SECONDS_ROOT + i, N_BITS_ROOT));
        hashPrevious = headersBase[^1].Hash;
      }

      blockchain.InsertHeadersInTree(headersBase);

      return IsRejected("NBits changed between retargets", () => blockchain.InsertHeadersInTree([CreateHeader(hashPrevious, UNIX_TIME_SECONDS_ROOT + TIMESPAN_ROOT_TO_HEADER_BEFORE_RETARGET_SECONDS, N_BITS_ROOT + 1)]), ref message)
        && IsRetargetCorrect(blockchain, hashPrevious, TIMESPAN_RETARGET_SECONDS, N_BITS_ROOT, N_BITS_TARGET_QUADRUPLED, ref message)
        && IsRetargetCorrect(blockchain, hashPrevious, 4 * TIMESPAN_RETARGET_SECONDS + 1, N_BITS_TARGET_QUADRUPLED, N_BITS_ROOT, ref message)
        && IsRetargetCorrect(blockchain, hashPrevious, TIMESPAN_ROOT_TO_HEADER_BEFORE_RETARGET_SECONDS, N_BITS_TARGET_QUARTERED, N_BITS_ROOT, ref message);
    }

    static bool IsRetargetCorrect(BlockchainBitcoin blockchain, byte[] hashPrevious, uint timespan, uint nBitsExpected, uint nBitsWrong, ref string message)
    {
      uint unixTimeSeconds = UNIX_TIME_SECONDS_ROOT + timespan;
      Header headerBeforeRetarget = CreateHeader(hashPrevious, unixTimeSeconds, N_BITS_ROOT);

      return IsRejected($"Retarget with timespan {timespan} and NBits {nBitsWrong:X8}", () => blockchain.InsertHeadersInTree([headerBeforeRetarget, CreateHeader(headerBeforeRetarget.Hash, unixTimeSeconds, nBitsWrong)]), ref message)
        && IsAccepted($"Retarget with timespan {timespan} and NBits {nBitsExpected:X8}", () => blockchain.InsertHeadersInTree([CreateHeader(hashPrevious, unixTimeSeconds, N_BITS_ROOT), CreateHeader(headerBeforeRetarget.Hash, unixTimeSeconds, nBitsExpected)]), ref message);
    }

    static BlockchainBitcoin CreateBlockchainBitcoin()
    {
      CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);

      return new BlockchainBitcoin(CreateLiteDatabase(), new SemaphoreSlim(1));
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

    static byte[] MineHeader(uint unixTimeSeconds)
    {
      HeaderBitcoin header = CreateHeader(new byte[32], unixTimeSeconds, N_BITS_EASY);

      while (header.Hash[^1] >= BYTE_HASH_MOST_SIGNIFICANT_BELOW_TARGET_EASY)
      {
        header.Nonce += 1;
        header.ComputeHash();
      }

      return header.Serialize();
    }

    static void ParseHeader(byte[] headerRaw)
    {
      int index = 0;
      new ParserBitcoin().ParseHeader(headerRaw, ref index);
    }
  }
}
