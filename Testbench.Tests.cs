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

  class MakeAnInstanceOfBitcoin : Test_Testbench
  {
    internal MakeAnInstanceOfBitcoin(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      Testbench.TokenBitcoin = new TokenBitcoin(Testbench);

      message = "";
      return true;
    }
  }

  class MakeAnInstanceOfBToken : Test_Testbench
  {
    internal MakeAnInstanceOfBToken(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      Testbench.TokenBToken = new TokenBToken(Testbench, Testbench.TokenBitcoin);

      message = "";
      return true;
    }
  }

  class StartBitcoin : Test_Testbench
  {
    internal StartBitcoin(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      Testbench.TokenBitcoin.Start();

      foreach (Peer peer in Testbench.TokenBitcoin.Network.Peers)
      {
        SocketTest socket = peer.SocketCommunication as SocketTest;

        if (socket.LogsSendMessage[0] != "version")
        {
          message = $"Bitcoin peer did not initiate version message when starting BToken.";
          return false;
        }
      }

      message = "";
      return true;
    }
  }

  class StartBToken : Test_Testbench
  {
    internal StartBToken(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      Testbench.TokenBToken.Start();

      foreach (Peer peer in Testbench.TokenBToken.Network.Peers)
      {
        SocketTest socket = peer.SocketCommunication as SocketTest;

        if (socket.LogsSendMessage[0] != "version")
        {
          message = $"BToken peer did not initiate version message when starting BToken.";
          return false;
        }
      }

      message = "";
      return true;
    }
  }

  class TestModuleBlockchain : Test_Testbench
  {
    Blockchain Blockchain;

    internal TestModuleBlockchain(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      Blockchain = new(Testbench.CreateHeaderGenesis());

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
      HeaderBitcoin header737857 = new(
         headerHash: "0000000000000000000735fc64773067fd0175461148902c97c494ffa3ca9306".ToBinary(),
         version: 0x21d18000,
         hashPrevious: "0000000000000000000230d9bb1db81e56916b0c2c7363231e75b82b24714482".ToBinary(),
         merkleRootHash: "5a38043063d4da8309c3cbb2ffd20db190d74bdce1dec3f716d2ffc553864717".ToBinary(),
         unixTimeSeconds: 1653491077,
         nBits: 0x17096a20,
         nonce: 0x42a47d62);
      HeaderBitcoin header737858 = new(
         headerHash: "0000000000000000000186f8f9c843648a0942e2eccc34e6614f48777451aa63".ToBinary(),
         version: 0x20200004,
         hashPrevious: "0000000000000000000735fc64773067fd0175461148902c97c494ffa3ca9306".ToBinary(),
         merkleRootHash: "41ccb67e2dffeabddd40c985f737fa7d62a6b2ab95a65712e6f18eb2a2b8e902".ToBinary(),
         unixTimeSeconds: 1653491090,
         nBits: 0x17096a20,
         nonce: 0x5a73d462);
      HeaderBitcoin header737859 = new(
         headerHash: "00000000000000000007808c3027b98d88750122d752776c8556e781c881673f".ToBinary(),
         version: 0x2f25a004,
         hashPrevious: "0000000000000000000186f8f9c843648a0942e2eccc34e6614f48777451aa63".ToBinary(),
         merkleRootHash: "ad6ff2ad97e6db66396e9cea3d838e2c2ba9162ae167682d61e008ec808343fc".ToBinary(),
         unixTimeSeconds: 1653491330,
         nBits: 0x17096a20,
         nonce: 0x9c299771);

      try
      {
        header737858.AppendToHeader(header737857);
        header737857.HeaderNext = header737858;

        header737859.AppendToHeader(header737858);
        header737858.HeaderNext = header737859;
      }
      catch (Exception ex)
      {
        messsage = $"{ex.GetType().Name} when appending headers:\n{ex.Message}";
        return false;
      }

      Blockchain chain = Blockchain.TryExtendHeaderchain(header737857);

      if (Blockchain.HeaderRoot.HeaderNext != header737857
        || header737857.HeaderNext != header737858
        || header737858.HeaderNext != header737859)
      {
        messsage = "TryExtendHeaderchain building chain incorrectly.";
        return false;
      }

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