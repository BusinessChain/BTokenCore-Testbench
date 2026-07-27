using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BTokenLib;


namespace BTokenCore_Testbench;

internal partial class Testbench
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
      Console.WriteLine("Create an instance of TokenBitcoin.");

      Testbench.TokenBitcoin = new TokenBitcoin(Testbench);

      message = "";
      return true;
    }
  }
}