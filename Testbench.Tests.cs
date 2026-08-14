using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BTokenCore;


namespace BTokenCore_Testbench;

public partial class Testbench
{
  abstract class Test_Testbench
  {
    protected Testbench Testbench;

    public Test_Testbench(Testbench testbench)
    {
      Testbench = testbench;
    }

    public abstract bool TryRun(out string message);
  }

  class MakeAnInstanceOfBitcoin : Test_Testbench
  {
    public MakeAnInstanceOfBitcoin(Testbench testbench)
      : base(testbench)
    { }

    public override bool TryRun(out string message)
    {
      Testbench.TokenBitcoin = new TokenBitcoin(Testbench);

      message = "";
      return true;
    }
  }

  class MakeAnInstanceOfBToken : Test_Testbench
  {
    public MakeAnInstanceOfBToken(Testbench testbench)
      : base(testbench)
    { }

    public override bool TryRun(out string message)
    {
      Testbench.TokenBToken = new TokenBToken(Testbench, Testbench.TokenBitcoin);

      message = "";
      return true;
    }
  }

  class StartBitcoin : Test_Testbench
  {
    public StartBitcoin(Testbench testbench)
      : base(testbench)
    { }

    public override bool TryRun(out string message)
    {
      message = "";

      Testbench.TokenBitcoin.Start();

      foreach (List<string> logsSendMessage in Testbench.TokenBitcoin.Network.GetLogsSendMessage())
      {
        if (logsSendMessage[0] != "version")
          message = "Did not initiate version message when starting Bitcoin.";
      }

      return true;
    }
  }

  class StartBToken : Test_Testbench
  {
    public StartBToken(Testbench testbench)
      : base(testbench)
    { }

    public override bool TryRun(out string message)
    {
      message = "";

      Testbench.TokenBToken.Start();

      foreach(List<string> logsSendMessage in Testbench.TokenBToken.Network.GetLogsSendMessage())
      {
        if (logsSendMessage[0] != "version")
          message = "Did not initiate version message when starting BToken.";
      }

      return true;
    }
  }
}