using System.Collections.Concurrent;
using System.Threading.Channels;

using BTokenCore;

namespace BTokenCore_Testbench;

internal partial class Testbench
{
  abstract partial class Test_Testbench
  {
    protected class SocketFake : ISocketCommunication
    {
      const int SECONDS_TIMEOUT_RECEIVE = 10;

      internal string IPRemote;
      SocketFake Partner;
      Channel<(string command, byte[] payload)> MessagesReceived = Channel.CreateUnbounded<(string command, byte[] payload)>();
      byte[] PayloadNext;
      TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);


      internal static SocketFake CreatePair(out SocketFake socketPeer)
      {
        SocketFake socketNode = new();
        socketPeer = new() { Partner = socketNode, Closed = socketNode.Closed };
        socketNode.Partner = socketPeer;

        return socketNode;
      }

      public Task Start(byte[] magicBytes)
      {
        return Task.CompletedTask;
      }

      public Task SendMessage(string commandString, int lengthDataPayload, byte[] payload)
      {
        if (!Partner.MessagesReceived.Writer.TryWrite((commandString, payload[..lengthDataPayload])))
          throw new IOException("Fake socket is closed.");

        return Task.CompletedTask;
      }

      public async Task<string> ReceiveCommandMessageNext()
      {
        (string command, byte[] payload) = await MessagesReceived.Reader.ReadAsync().ConfigureAwait(false);

        PayloadNext = payload;

        return command;
      }

      public Task<int> ReceivePayloadNext(byte[] buffer)
      {
        if (PayloadNext.Length > buffer.Length)
          throw new ProtocolException($"Payload length {PayloadNext.Length} exceeds buffer size {buffer.Length}.");

        PayloadNext.CopyTo(buffer, 0);

        return Task.FromResult(PayloadNext.Length);
      }

      public void Dispose()
      {
        MessagesReceived.Writer.TryComplete();
        Partner.MessagesReceived.Writer.TryComplete();
        Closed.TrySetResult();
      }

      internal bool IsDisconnected(string nameCase, ref string message, int secondsTimeout = SECONDS_TIMEOUT_RECEIVE)
      {
        if (Closed.Task.Wait(TimeSpan.FromSeconds(secondsTimeout)))
          return true;

        message = $"{nameCase}: the node did not disconnect the peer within {secondsTimeout} seconds.";
        return false;
      }

      public string GetIP()
      {
        return IPRemote;
      }

      internal bool TryReceive(string nameCase, string command, out byte[] payload, ref string message)
      {
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(SECONDS_TIMEOUT_RECEIVE));

        try
        {
          (string command, byte[] payload) messageReceived;

          do
            messageReceived = MessagesReceived.Reader.ReadAsync(cancellation.Token).AsTask().GetAwaiter().GetResult();
          while (messageReceived.command != command);

          payload = messageReceived.payload;
          return true;
        }
        catch (Exception exception) when (exception is OperationCanceledException or ChannelClosedException)
        {
          payload = null;
          message = $"{nameCase}: the node sent no {command} message within {SECONDS_TIMEOUT_RECEIVE} seconds.";
          return false;
        }
      }
    }

    protected class CommunicationFake : ICommunication
    {
      ConcurrentQueue<SocketFake> SocketsOutbound = new();
      Channel<SocketFake> SocketsInbound = Channel.CreateUnbounded<SocketFake>();
      ConcurrentQueue<string> AddressesRequested = new();


      internal SocketFake QueueOutbound()
      {
        SocketsOutbound.Enqueue(SocketFake.CreatePair(out SocketFake socketPeer));

        return socketPeer;
      }

      internal SocketFake ConnectInbound(string iPPeer)
      {
        SocketFake socketNode = SocketFake.CreatePair(out SocketFake socketPeer);
        socketNode.IPRemote = iPPeer;

        SocketsInbound.Writer.TryWrite(socketNode);

        return socketPeer;
      }

      public void StartListenerCommunicationInbound(int port)
      { }

      public async Task<ISocketCommunication> AcceptSocketCommunicationInbound()
      {
        return await SocketsInbound.Reader.ReadAsync().ConfigureAwait(false);
      }

      internal bool IsRequested(string address, int secondsTimeout)
      {
        return SpinWait.SpinUntil(() => AddressesRequested.Contains(address), TimeSpan.FromSeconds(secondsTimeout));
      }

      public ISocketCommunication GetSocketCommunication(int port, string address)
      {
        AddressesRequested.Enqueue(address);

        if (!SocketsOutbound.TryDequeue(out SocketFake socketNode))
          throw new IOException($"No outbound connection to {address} queued.");

        socketNode.IPRemote = address;

        return socketNode;
      }
    }
  }
}
