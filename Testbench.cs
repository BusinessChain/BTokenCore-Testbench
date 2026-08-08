using System;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

using BTokenCore;


namespace BTokenCore_Testbench;

partial class Testbench : IEnvironment
{
  List<Test_Testbench> Tests;

  TokenBitcoin TokenBitcoin;
  TokenBToken TokenBToken;


  public Testbench()
  {
    LoadTests();
  }

  public async Task<ISocketCommunication> GetSocketCommunication(Token token, string address)
  {
    ISocketCommunication networkAdapterTCP = new NetworkAdapterTCP(address, token.Port);

    await networkAdapterTCP.Start();

    return networkAdapterTCP;
  }

  TcpListener TcpListener;

  public void StartListenerCommunicationInbound(int port)
  {
    TcpListener = new(IPAddress.Any, port);
    TcpListener.Start(1);
  }

  public async Task<ISocketCommunication> AcceptSocketCommunicationInbound()
  {
    TcpClient tcpClient = await TcpListener.AcceptTcpClientAsync().ConfigureAwait(false);

    return new NetworkAdapterTCP(tcpClient);
  }

  void LoadTests()
  {
    Tests = new()
    {
      new MakeAnInstanceOfBitcoin(this),
      new MakeAnInstanceOfBToken(this),
      new MakeAnInstanceOfNetworkAdapterTCP(this),
      new StartBToken(this)
    };
  }

  public void Start()
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
        Console.Write($"Run test {i + 1} : {test.GetType().Name}");

        resultIsSuccess = test.TryRun(out message);

        if (resultIsSuccess)
        {
          Console.WriteLine($" -- success.");
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

    Console.WriteLine($"All tests succeded, congratulations !");
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
