using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
  }

  class TestParsers : Test_Testbench
  {
    internal TestParsers(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      return Test_RoundTripHeaderBToken(ref message);
    }

    bool Test_RoundTripHeaderBToken(ref string message)
    {
      HeaderBToken header = new(
        headerHash: null,
        hashPrevious: "00000000000000000002a7c4c1e48d76c5a37902165a270156b7a8d72728a054".ToBinary(),
        merkleRootHash: "5a38043063d4da8309c3cbb2ffd20db190d74bdce1dec3f716d2ffc553864717".ToBinary(),
        hashAccounts: "6e340b9cffb37a989ca544e6bb780a2c78901d3fb33738768511a30617afa01d".ToBinary(),
        nonce: 0x42a47d62);

      header.Version = 0x21d18000;
      header.ComputeHash();

      byte[] buffer = header.Serialize();
      int index = 0;

      HeaderBToken headerParsed = new(buffer, ref index);

      return IsFieldEqual(nameof(HeaderBToken), nameof(Header.Version), header.Version, headerParsed.Version, ref message)
        && IsFieldEqual(nameof(HeaderBToken), nameof(Header.HashPrevious), header.HashPrevious, headerParsed.HashPrevious, ref message)
        && IsFieldEqual(nameof(HeaderBToken), nameof(Header.MerkleRoot), header.MerkleRoot, headerParsed.MerkleRoot, ref message)
        && IsFieldEqual(nameof(HeaderBToken), nameof(HeaderBToken.HashAccounts), header.HashAccounts, headerParsed.HashAccounts, ref message)
        && IsFieldEqual(nameof(HeaderBToken), nameof(Header.Nonce), header.Nonce, headerParsed.Nonce, ref message)
        && IsFieldEqual(nameof(HeaderBToken), nameof(Header.Hash), header.Hash, headerParsed.Hash, ref message)
        && IsFieldEqual(nameof(HeaderBToken), "index after parsing", HeaderBToken.COUNT_HEADER_BYTES, index, ref message);
    }

    static bool IsFieldEqual(string nameParser, string nameField, byte[] expected, byte[] parsed, ref string message)
    {
      if (expected.AsSpan().SequenceEqual(parsed))
        return true;

      message = $"{nameParser}: field {nameField} is {parsed.ToHexString()} after parsing, expected {expected.ToHexString()}.";
      return false;
    }

    static bool IsFieldEqual<T>(string nameParser, string nameField, T expected, T parsed, ref string message)
      where T : struct
    {
      if (EqualityComparer<T>.Default.Equals(expected, parsed))
        return true;

      message = $"{nameParser}: field {nameField} is {parsed} after parsing, expected {expected}.";
      return false;
    }
  }

  class MakeAnInstanceOfNode : Test_Testbench
  {
    internal MakeAnInstanceOfNode(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      Testbench.Node = new Node(Testbench);

      message = "";
      return true;
    }
  }

  class StartNode : Test_Testbench
  {
    internal StartNode(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      Testbench.Node.Start();
            
      message = "";
      return true;
    }
  }

  class TestModuleBlockchain : Test_Testbench
  {
    //Branch Blockchain;

    internal TestModuleBlockchain(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      //Blockchain = new(Testbench.CreateHeaderGenesis());

      message = "";

      if(Test_TryExtendHeaderchain(ref message)
        //&& Test_GetHeader(ref message)
        //&& Test_AppendHeader(ref message)
        //&& Test_FetchHeaderDownload(ref message)
        //&& Test_InsertBlockInChain(ref message)
        //&& Test_TryGetBlockNext(ref message)
        //&& Test_GetLocator(ref message)
        //&& Test_GetHeadersSerialized(ref message)
        )
        return true;

      return false;
    }

    bool Test_TryExtendHeaderchain(ref string messsage)
    {
      //HeaderBitcoin header737857 = new(
      //   headerHash: "0000000000000000000735fc64773067fd0175461148902c97c494ffa3ca9306".ToBinary(),
      //   version: 0x21d18000,
      //   hashPrevious: "0000000000000000000230d9bb1db81e56916b0c2c7363231e75b82b24714482".ToBinary(),
      //   merkleRootHash: "5a38043063d4da8309c3cbb2ffd20db190d74bdce1dec3f716d2ffc553864717".ToBinary(),
      //   unixTimeSeconds: 1653491077,
      //   nBits: 0x17096a20,
      //   nonce: 0x42a47d62);
      //HeaderBitcoin header737858 = new(
      //   headerHash: "0000000000000000000186f8f9c843648a0942e2eccc34e6614f48777451aa63".ToBinary(),
      //   version: 0x20200004,
      //   hashPrevious: "0000000000000000000735fc64773067fd0175461148902c97c494ffa3ca9306".ToBinary(),
      //   merkleRootHash: "41ccb67e2dffeabddd40c985f737fa7d62a6b2ab95a65712e6f18eb2a2b8e902".ToBinary(),
      //   unixTimeSeconds: 1653491090,
      //   nBits: 0x17096a20,
      //   nonce: 0x5a73d462);
      //HeaderBitcoin header737859 = new(
      //   headerHash: "00000000000000000007808c3027b98d88750122d752776c8556e781c881673f".ToBinary(),
      //   version: 0x2f25a004,
      //   hashPrevious: "0000000000000000000186f8f9c843648a0942e2eccc34e6614f48777451aa63".ToBinary(),
      //   merkleRootHash: "ad6ff2ad97e6db66396e9cea3d838e2c2ba9162ae167682d61e008ec808343fc".ToBinary(),
      //   unixTimeSeconds: 1653491330,
      //   nBits: 0x17096a20,
      //   nonce: 0x9c299771);

      //try
      //{
      //  header737858.TryAppendToHeader(header737857);
      //  header737857.HeaderNext = header737858;

      //  header737859.TryAppendToHeader(header737858);
      //  header737858.HeaderNext = header737859;
      //}
      //catch (Exception ex)
      //{
      //  messsage = $"{ex.GetType().Name} when appending headers:\n{ex.Message}";
      //  return false;
      //}

      //Blockchain chain = Blockchain.TryExtendHeaderchain(header737857);

      //if (Blockchain.HeaderRoot.HeaderNext != header737857
      //  || header737857.HeaderNext != header737858
      //  || header737858.HeaderNext != header737859)
      //{
      //  messsage = "TryExtendHeaderchain building chain incorrectly.";
      //  return false;
      //}

      return true;
    }

    bool Test_GetHeader(ref string messsage)
    {
      return false;
    }

    bool Test_AppendHeader(ref string messsage)
    {
      return false;
    }

    bool Test_FetchHeaderDownload(ref string messsage)
    {
      return false;
    }

    bool Test_InsertBlockInChain(ref string messsage)
    {
      return false;
    }

    bool Test_TryGetBlockNext(ref string messsage)
    {
      return false;
    }

    bool Test_GetLocator(ref string messsage)
    {
      return false;
    }

    bool Test_GetHeadersSerialized(ref string messsage)
    {
      return false;
    }
  }
}