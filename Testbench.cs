using System.Diagnostics;

using BTokenCore;


namespace BTokenCore_Testbench;

internal partial class Testbench
{
  List<Test_Testbench> Tests;

  internal Wallet Wallet;

  const string NAME_FILE_LOG_REAL_PEERS = "TestRealPeers.log";


  class TextWriterTee : TextWriter
  {
    TextWriter WriterConsole;
    TextWriter WriterLog;


    internal TextWriterTee(TextWriter writerConsole, TextWriter writerLog)
    {
      WriterConsole = writerConsole;
      WriterLog = writerLog;
    }

    public override System.Text.Encoding Encoding
    {
      get { return WriterConsole.Encoding; }
    }

    public override void Write(char value)
    {
      WriterConsole.Write(value);
      WriterLog.Write(value);
    }

    public override void Write(string value)
    {
      WriterConsole.Write(value);
      WriterLog.Write(value);
    }

    public override void WriteLine(string value)
    {
      WriterConsole.WriteLine(value);
      WriterLog.WriteLine(value);
    }
  }

  internal Testbench(string[] args)
  {
    if (args.Contains("real-peers"))
    {
      StreamWriter writerLog = new(Path.Combine(AppContext.BaseDirectory, NAME_FILE_LOG_REAL_PEERS), append: false) { AutoFlush = true };
      Console.SetOut(TextWriter.Synchronized(new TextWriterTee(Console.Out, writerLog)));

      Tests = new()
      {
        new TestRealPeers(this)
      };

      return;
    }

    Tests = new()
    {
      new TestParsers(this),
      new TestBlockchainBitcoin(this),
      new TestConsensusRules(this),
      new TestTXPool(this),
      new TestAccountSynchronizer(this),
      new TestAnchoring(this),
      new TestMiner(this),
      new TestNetwork(this),
      new TestRPCServer(this)
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
