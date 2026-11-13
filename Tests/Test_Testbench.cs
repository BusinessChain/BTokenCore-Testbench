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
}
