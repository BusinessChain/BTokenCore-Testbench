using System;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

using BTokenCore;


namespace BTokenCore_Testbench;

internal partial class Testbench : IEnvironment
{
  List<Test_Testbench> Tests;

  TokenBitcoin TokenBitcoin;
  TokenBToken TokenBToken;


  internal Testbench()
  {
    Tests = new()
    {
      new MakeAnInstanceOfBitcoin(this),
      new MakeAnInstanceOfBToken(this),
      new StartBitcoin(this),
      new StartBToken(this)
    };
  }

  //public ISocketCommunication GetSocketCommunication(Token token, string address)
  //{
  //  return new NetworkAdapterTCP(address, token.Port);
  //}

  public ISocketCommunication GetSocketCommunication(Token token, string address)
  {
    return new SocketTest(address);
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

    return new SocketTCP(tcpClient);
  }

  internal void Start()
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
