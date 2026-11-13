using System.Diagnostics;

using BTokenCore;


namespace BTokenCore_Testbench;

internal partial class Testbench
{
  List<Test_Testbench> Tests;

  internal Wallet Wallet;


  internal Testbench()
  {
    Tests = new()
    {
      new TestParsers(this),
      new TestConsensusRules(this)
    };
  }

  internal void Start()
  {
    string pathWallet = Path.GetTempFileName();

    try
    {
      File.WriteAllText(pathWallet, Crypto.GenerateKeyPrivate());

      Wallet = new(pathWallet);

      RunTests();
    }
    finally
    {
      File.Delete(pathWallet);
    }
  }

  void RunTests()
  {
    bool resultIsSuccess = false;
    string message = "";
    Test_Testbench test;

    Console.WriteLine($"Start testbench, {Tests.Count} tests in total.\n");

    for (int i = 0; i < Tests.Count; i++)
    {
      test = Tests[i];

      try
      {
        Console.WriteLine($"Run test {i + 1} : {test.GetType().Name}");

        resultIsSuccess = test.TryRun(out message);

        if (resultIsSuccess)
        {
          Console.WriteLine($"  -- success.");
          continue;
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine($"\n{ex.GetType().Name}: {ex.Message}");
        PrintStackTrace(ex);
      }

      Console.WriteLine($"\nTest {i + 1} ({Tests[i].GetType().Name}) failed.");

      if(message != "")
        Console.WriteLine(message);

      return;
    }

    Console.WriteLine($"\nAll tests succeded, congratulations !");
  }

  static void PrintStackTrace(Exception ex)
  {
    StackTrace stackTrace = new(ex, true);

    foreach (StackFrame frame in stackTrace.GetFrames() ?? [])
    {
      var method = frame.GetMethod();
      var type = method?.DeclaringType;

      // Skip framework code.
      if (type?.Namespace == null || !type.Namespace.StartsWith("BToken"))
        continue;

      Console.WriteLine(
          $"   at {type.FullName}.{method!.Name} " +
          $"in {frame.GetFileName()}:line {frame.GetFileLineNumber()}");
    }
  }
}
