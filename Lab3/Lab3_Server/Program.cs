using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Lab3_Server
{
    internal class Program
    {
        private static ConcurrentDictionary<string, TcpClient> _clients = new ConcurrentDictionary<string, TcpClient>();
        private static int _tcpPort;
        private static int _udpPort;

        static async Task Main(string[] args)
        {
            Console.WriteLine("<<< СЕРВЕР >>>");

            Console.Write("Введите IP-адрес сервера (пример: 127.0.0.1): ");
            string ipInput = Console.ReadLine();
            IPAddress serverIp = IPAddress.Parse(ipInput);

            Console.Write("Введите порт для работы чата: ");
            _tcpPort = int.Parse(Console.ReadLine());
            _udpPort = _tcpPort + 1;

            if(!IsPortAvailable(_tcpPort) || !IsPortAvailable(_udpPort))
            {
                Console.WriteLine($"[Ошибка] Порт {_tcpPort} или {_udpPort} уже занят другой программой.");
                return;
            }

            TcpListener tcpListener = new TcpListener(serverIp, _tcpPort);
            tcpListener.Start();
            Console.WriteLine($"\n[Сервер запущен] Ожидание подключений на {serverIp}:{_tcpPort}");
            Console.WriteLine($"[UDP Уведомления] Будут рассылаться на порт {_udpPort}");

            while (true)
            {
                TcpClient client = await tcpListener.AcceptTcpClientAsync();

                _ = Task.Run(() => HandleClientAsync(client));
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            string clientEndPoint = client.Client.RemoteEndPoint.ToString();
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

            _clients.TryAdd(clientEndPoint, client);
            Console.WriteLine($"[Подключение] {clientEndPoint} присоединился к чату.");

            SendUdpNotification($"Новый участник присоединился к чату: {clientIp}");

            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];

            try
            {
                while (true) { 
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"[TCP Сообщение от {clientEndPoint}]: {message}");

                    BroadcastTcpMessage(message, clientEndPoint);
                }
            }
            catch (Exception ) { }
            finally 
            {
                _clients.TryRemove(clientEndPoint, out _);
                client.Close();
                Console.WriteLine($"[Отключение] {clientEndPoint} покинул чат.");

                SendUdpNotification($"Участник покинул чат: {clientIp}");
            }
        }

        private static void BroadcastTcpMessage(string message, string senderEndPoint)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            foreach (var client in _clients)
            {
                if (client.Key != senderEndPoint) 
                {
                    try
                    {
                        client.Value.GetStream().Write(data, 0, data.Length);
                    }
                    catch { }
                }
            }
        }

        private static void SendUdpNotification(string message)
        {
            using (UdpClient udpClient = new UdpClient())
            {
                byte[] data = Encoding.UTF8.GetBytes(message);

                var uniqueIps = _clients.Values
                    .Select(c => ((IPEndPoint)c.Client.RemoteEndPoint).Address)
                    .Distinct();

                foreach (var ip in uniqueIps)
                {
                    try
                    {
                        IPEndPoint endPoint = new IPEndPoint(ip, _udpPort);
                        udpClient.Send(data, data.Length, endPoint);
                    }
                    catch {  }
                }
            }
        }
        private static bool IsPortAvailable(int port)
        {
            IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            TcpConnectionInformation[] tcpConnInfoArray = ipGlobalProperties.GetActiveTcpConnections();

            foreach (TcpConnectionInformation tcpi in tcpConnInfoArray)
            {
                if (tcpi.LocalEndPoint.Port == port) return false;
            }

            IPEndPoint[] tcpListeners = ipGlobalProperties.GetActiveTcpListeners();
            foreach (IPEndPoint ep in tcpListeners)
            {
                if (ep.Port == port) return false;
            }

            return true;
        }
    }
}
