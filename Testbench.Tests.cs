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
}