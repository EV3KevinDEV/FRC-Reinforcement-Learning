using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace MoSimRL
{
    /// <summary>
    /// Length-prefixed TCP server. This class never calls Unity APIs from its
    /// network thread; parsed requests are consumed by RlEnvironmentController.
    /// </summary>
    public sealed class RlTcpServer : IDisposable
    {
        private const int MaxFrameBytes = 1 << 20;
        private readonly IPAddress _address;
        private readonly int _port;
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private readonly ConcurrentQueue<byte[]> _requests = new();
        private readonly object _writeLock = new();
        private TcpListener _listener;
        private TcpClient _client;
        private Thread _thread;
        private volatile bool _stopping;

        public RlTcpServer(string host, int port)
        {
            if (!IPAddress.TryParse(host, out _address))
            {
                _address = IPAddress.Loopback;
            }
            _port = port;
        }

        public void Start()
        {
            _listener = new TcpListener(_address, _port);
            _listener.Start(1);
            _thread = new Thread(ServerLoop)
            {
                IsBackground = true,
                Name = $"MoSim RL TCP {_port}"
            };
            _thread.Start();
        }

        public bool TryDequeue(out RlRequest request)
        {
            request = null;
            while (_requests.TryDequeue(out var payload))
            {
                try
                {
                    request = JsonUtility.FromJson<RlRequest>(StrictUtf8.GetString(payload));
                    if (request == null || request.id <= 0 || string.IsNullOrWhiteSpace(request.cmd))
                    {
                        throw new InvalidDataException("Request envelope is incomplete.");
                    }
                    request.payload ??= new RlRequestPayload();
                    return true;
                }
                catch (Exception)
                {
                    Send(new RlResponse
                    {
                        id = 0,
                        ok = false,
                        error = "invalid_request",
                        payload = new RlResponsePayload()
                    });
                }
            }
            return false;
        }

        public void Send(RlResponse response)
        {
            var json = JsonUtility.ToJson(response);
            var payload = Encoding.UTF8.GetBytes(json);
            if (payload.Length == 0 || payload.Length > MaxFrameBytes)
            {
                throw new InvalidDataException($"RL response length {payload.Length} is invalid.");
            }

            lock (_writeLock)
            {
                if (_client == null || !_client.Connected)
                {
                    return;
                }

                try
                {
                    var stream = _client.GetStream();
                    var length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));
                    stream.Write(length, 0, length.Length);
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush();
                }
                catch (IOException)
                {
                    CloseClient();
                }
                catch (SocketException)
                {
                    CloseClient();
                }
            }
        }

        private void ServerLoop()
        {
            while (!_stopping)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    client.NoDelay = true;
                    lock (_writeLock)
                    {
                        CloseClient();
                        _client = client;
                    }
                    ReadClient(client);
                }
                catch (SocketException)
                {
                    if (!_stopping)
                    {
                        Thread.Sleep(100);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private void ReadClient(TcpClient client)
        {
            var stream = client.GetStream();
            var header = new byte[4];
            while (!_stopping && client.Connected)
            {
                if (!ReadExact(stream, header, header.Length))
                {
                    break;
                }
                var length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header, 0));
                if (length <= 0 || length > MaxFrameBytes)
                {
                    break;
                }
                var payload = new byte[length];
                if (!ReadExact(stream, payload, payload.Length))
                {
                    break;
                }

                _requests.Enqueue(payload);
            }
            lock (_writeLock)
            {
                if (_client == client)
                {
                    CloseClient();
                }
            }
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                int read;
                try
                {
                    read = stream.Read(buffer, offset, count - offset);
                }
                catch (IOException)
                {
                    return false;
                }
                if (read <= 0)
                {
                    return false;
                }
                offset += read;
            }
            return true;
        }

        private void CloseClient()
        {
            try
            {
                _client?.Close();
            }
            catch (SocketException)
            {
                // The peer already disconnected.
            }
            _client = null;
        }

        public void Dispose()
        {
            _stopping = true;
            try
            {
                _listener?.Stop();
            }
            catch (SocketException)
            {
                // Listener is already closed.
            }
            lock (_writeLock)
            {
                CloseClient();
            }
            if (_thread is { IsAlive: true })
            {
                _thread.Join(1000);
            }
        }
    }
}
