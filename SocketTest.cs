using System;

using BTokenCore;


namespace BTokenCore_Testbench;

public class SocketTest : ISocketCommunication
{
  string ID;

  public SocketTest(string id)
  {
    ID = id;
  }

  public async Task Start()
  {

  }

  public List<string> LogSendMessage = new();
  public async Task SendMessage(string commandString, int lengthDataPayload, byte[] payload)
  {
    LogSendMessage.Add(commandString);
  }

  public async Task<string> ReceiveCommandMessageNext()
  {
    await Task.Delay(-1).ConfigureAwait(false);
    return "test";
  }

  public async Task LoadMessageNext(MessageNetworkProtocol message)
  {

  }

  public void Dispose()
  {

  }

  public string GetIP()
  {
    return ID;
  }
}
