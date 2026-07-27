using System;
using System.Linq;
using System.Text;
using System.Diagnostics;

using BTokenLib;


namespace BTokenCore_Testbench;


partial class Testbench : ILogEntryNotifier
{
  List<Test_Testbench> Tests;

  TokenBitcoin TokenBitcoin;


  public Testbench()
  {
    LoadTests();
  }


  void LoadTests()
  {
    Tests = new()
    {
      new MakeAnInstanceOfBitcoin(this)
    };
  }

  public void Start()
  {
    bool resultIsSuccess = false;
    string message = "";
    Test_Testbench test;

    for (int i = 0; i < Tests.Count; i++)
    {
      test = Tests[i];

      try
      {
        resultIsSuccess = test.TryRun(out message);

        if (resultIsSuccess)
          continue;
      }
      catch (Exception ex)
      {
        Console.WriteLine($"\n{ex.GetType().Name}: {ex.Message}");
        PrintStackTrace(ex);
      }

      Console.WriteLine($"\n\nTest {i + 1} ({Tests[i].GetType().Name}) of {Tests.Count} tests failed.");

      if(message != "")
        Console.WriteLine(message);

      return;
    }

    Console.WriteLine($"All ({Tests.Count}) tests succeded, congratulations !");
  }

  public void NotifyLogEntry(string logEntry, string source)
  {
    Console.WriteLine($"{source}: {logEntry}");
  }

  static void PrintStackTrace(Exception ex)
  {
    Console.WriteLine($"\nStack trace:\n");

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
