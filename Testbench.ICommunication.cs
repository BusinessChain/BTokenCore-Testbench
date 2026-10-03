using System.Net;
using System.Net.Sockets;
using BTokenCore;


namespace BTokenCore_Testbench;

internal partial class Testbench : ICommunication
{
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

  //public ISocketCommunication GetSocketCommunication(int port, string address)
  //{
  //  return new NetworkAdapterTCP(address, port);
  //}

  public ISocketCommunication GetSocketCommunication(int port, string address)
  {
    return new SocketTest(address);
  }
}
