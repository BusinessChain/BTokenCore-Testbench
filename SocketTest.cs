using System;

using BTokenCore;


namespace BTokenCore_Testbench;

internal class SocketTest : ISocketCommunication
{
  internal string ID;
  internal List<string> LogsSendMessage = new();

  internal SocketTest(string id)
  {
    ID = id;
  }

  public async Task Start()
  {

  }

  public async Task SendMessage(string commandString, int lengthDataPayload, byte[] payload)
  {
    LogsSendMessage.Add(commandString);
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
