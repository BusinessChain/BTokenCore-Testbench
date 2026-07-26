using System;
using System.Linq;
using System.Text;
using System.Diagnostics;
using BTokenLib;


namespace BTokenCore_Testbench;


class Testbench : ILogEntryNotifier
{
  public Testbench()
  { }

  public void Start()
  {
    Console.WriteLine($"Test 1: Make an instance of TokenBitcoin.");
    
    try
    {
      TokenBitcoin tokenBitcoin = new TokenBitcoin(this);

      Console.WriteLine($"Test 1 result: success");
    }
    catch(Exception ex)
    {
      Console.WriteLine($"Test 1 result: fail");
      Console.WriteLine($"\n{ex.GetType().Name}: {ex.Message}");
      Console.WriteLine($"\nStack trace:\n");

      PrintStackTrace(ex);
    }
  }

  public void NotifyLogEntry(string logEntry, string source)
  {
    Console.WriteLine($"{source}: {logEntry}");
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
