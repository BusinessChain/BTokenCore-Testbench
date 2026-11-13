using BTokenCore;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  abstract class Test_Testbench
  {
    protected Testbench Testbench;

    internal Test_Testbench(Testbench testbench)
    {
      Testbench = testbench;
    }

    internal abstract bool TryRun(out string message);

    protected static HeaderBToken CreateHeaderBToken(byte[] merkleRoot)
    {
      HeaderBToken header = new(
        headerHash: null,
        hashPrevious: "00000000000000000002a7c4c1e48d76c5a37902165a270156b7a8d72728a054".ToBinary(),
        merkleRootHash: merkleRoot,
        hashAccounts: "6e340b9cffb37a989ca544e6bb780a2c78901d3fb33738768511a30617afa01d".ToBinary(),
        nonce: 0x42a47d62);

      header.Version = 0x21d18000;
      header.ComputeHash();

      return header;
    }

    protected static BlockchainBToken.TXBToken CreateTXBToken(byte[] keyPublic, byte[] iDAccountReceiving, params TXOutputTokenAnchor[] tXOutputsTokenAnchor)
    {
      BlockchainBToken.TXBToken tX = new()
      {
        KeyPublic = keyPublic,
        HeightAccountCreated = 1234,
        Nonce = 5,
        Fee = 1000,
        LockTime = 737857
      };

      tX.TXOutputs.Add(new TXOutputP2PKH(50000, iDAccountReceiving));
      tX.TXOutputs.AddRange(tXOutputsTokenAnchor);

      return tX;
    }

    protected static bool IsFieldEqual(string nameParser, string nameField, byte[] expected, byte[] parsed, ref string message)
    {
      if (expected.AsSpan().SequenceEqual(parsed))
        return true;

      message = $"{nameParser}: field {nameField} is {parsed.ToHexString()} after parsing, expected {expected.ToHexString()}.";
      return false;
    }

    protected static bool IsFieldEqual<T>(string nameParser, string nameField, T expected, T parsed, ref string message)
      where T : struct
    {
      if (EqualityComparer<T>.Default.Equals(expected, parsed))
        return true;

      message = $"{nameParser}: field {nameField} is {parsed} after parsing, expected {expected}.";
      return false;
    }
  }
}
