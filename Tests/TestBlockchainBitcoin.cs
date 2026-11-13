using BTokenCore;


namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestBlockchainBitcoin : Test_Testbench
  {
    const int INDEX_NONCE = 76;
    const uint N_BITS_EASY = 0x207FFFFF;
    const byte BYTE_HASH_MOST_SIGNIFICANT_BELOW_TARGET_EASY = 0x7F;

    internal TestBlockchainBitcoin(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return Test_ProofOfWork(ref message)
        && Test_TimestampPremature(ref message);
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

    static byte[] MineHeader(uint unixTimeSeconds)
    {
      HeaderBitcoin header = new(
        headerHash: null,
        version: 0x20000000,
        hashPrevious: new byte[32],
        merkleRootHash: new byte[32],
        unixTimeSeconds: unixTimeSeconds,
        nBits: N_BITS_EASY,
        nonce: 0);

      while (Crypto.ComputeHash256(header.Serialize())[^1] >= BYTE_HASH_MOST_SIGNIFICANT_BELOW_TARGET_EASY)
        header.Nonce += 1;

      return header.Serialize();
    }

    static void ParseHeader(byte[] headerRaw)
    {
      int index = 0;
      new ParserBitcoin().ParseHeader(headerRaw, ref index);
    }
  }
}
