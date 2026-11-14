using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using BTokenCore;


namespace BTokenCore_Testbench;

internal partial class Testbench
{
  class TestRPCServer : Test_Testbench
  {
    const string NAME_TOKEN_BTOKEN = "TokenBToken";
    const string TX_RAW_INVALID = "00";

    HttpClient HttpClient = new();
    string URLRPC;


    internal TestRPCServer(Testbench testbench)
      : base(testbench)
    { }

    internal override bool TryRun(out string message)
    {
      message = "";

      string directoryPrevious = Directory.GetCurrentDirectory();
      string directoryNode = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

      Directory.CreateDirectory(directoryNode);

      try
      {
        Directory.SetCurrentDirectory(directoryNode);

        CreateWalletFile(BlockchainBitcoin.NAME_TOKEN);
        CreateWalletFile(NAME_TOKEN_BTOKEN);

        int portRPC = GetPortFree();
        URLRPC = $"http://127.0.0.1:{portRPC}/";

        Node node = new(new CommunicationFake(), portRPC);
        node.Start().GetAwaiter().GetResult();

        return Test_Methods(ref message)
          && Test_SubmitTXBToken(node, ref message)
          && Test_Errors(ref message);
      }
      finally
      {
        Directory.SetCurrentDirectory(directoryPrevious);
      }
    }

    static int GetPortFree()
    {
      TcpListener tcpListener = new(IPAddress.Loopback, 0);
      tcpListener.Start();

      int port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;

      tcpListener.Stop();

      return port;
    }

    bool Test_Methods(ref string message)
    {
      Console.WriteLine("  1. RPC Methods: startMining and stopMining return results with their ids.");

      return IsResult("startMining", 1, Call("""{"jsonrpc":"2.0","id":1,"method":"startMining","params":{"countAnchorsPer10Minutes":1,"feeAnchor":1000}}"""), ref message)
        && IsResult("stopMining", 2, Call("""{"jsonrpc":"2.0","id":2,"method":"stopMining"}"""), ref message);
    }

    bool Test_SubmitTXBToken(Node node, ref string message)
    {
      Console.WriteLine("  2. RPC Submit: submitTXBToken returns Node's error for an invalid TX.");

      string messageNode;

      try
      {
        node.SubmitTXBToken(Convert.FromHexString(TX_RAW_INVALID)).GetAwaiter().GetResult();
        message = "submitTXBToken: Node accepted the invalid TX.";
        return false;
      }
      catch (Exception exception)
      {
        messageNode = exception.Message;
      }

      return IsError("submitTXBToken", messageNode, Call($$$"""{"jsonrpc":"2.0","id":3,"method":"submitTXBToken","params":{"tXRaw":"{{{TX_RAW_INVALID}}}"}}"""), ref message);
    }

    bool Test_Errors(ref string message)
    {
      Console.WriteLine("  3. RPC Errors: Faulty requests return errors, RPCServer keeps answering.");

      return IsError("unknown method", "Method getNothing not found.", Call("""{"jsonrpc":"2.0","id":4,"method":"getNothing"}"""), ref message)
        && IsError("startMining without parameters", null, Call("""{"jsonrpc":"2.0","id":5,"method":"startMining"}"""), ref message)
        && IsError("body without JSON", null, Call("no json"), ref message)
        && IsResult("stopMining after errors", 6, Call("""{"jsonrpc":"2.0","id":6,"method":"stopMining"}"""), ref message);
    }

    JsonElement Call(string body)
    {
      using StringContent content = new(body, Encoding.UTF8, "application/json");
      using HttpResponseMessage response = HttpClient.PostAsync(URLRPC, content).GetAwaiter().GetResult();
      using JsonDocument document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

      return document.RootElement.Clone();
    }

    static bool IsResult(string nameCase, int id, JsonElement response, ref string message)
    {
      if (response.TryGetProperty("error", out JsonElement error))
        message = $"{nameCase}: RPCServer returned the error {error}.";
      else if (!response.TryGetProperty("result", out _))
        message = $"{nameCase}: the response {response} carries no result.";
      else if (response.GetProperty("id").ValueKind != JsonValueKind.Number || response.GetProperty("id").GetInt32() != id)
        message = $"{nameCase}: the response {response} does not carry the id {id}.";
      else
        return true;

      return false;
    }

    static bool IsError(string nameCase, string messageExpected, JsonElement response, ref string message)
    {
      if (!response.TryGetProperty("error", out JsonElement error))
        message = $"{nameCase}: the response {response} carries no error.";
      else if (messageExpected != null && error.GetProperty("message").GetString() != messageExpected)
        message = $"{nameCase}: the error message is \"{error.GetProperty("message").GetString()}\" instead of \"{messageExpected}\".";
      else
        return true;

      return false;
    }
  }
}