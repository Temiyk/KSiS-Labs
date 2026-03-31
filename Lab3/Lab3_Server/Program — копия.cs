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

            IPAddress serverIp = GetValidIpAddress("Введите IP-адрес сервера (пример: 127.0.0.1): ");
            _tcpPort = GetValidPort("Введите TCP-порт для работы чата (например, 5000): ");
            _udpPort = GetValidPort("Введите UDP-порт для рассылки уведомлений (например, 5001): ");

            if (!IsPortAvailable(_tcpPort) || !IsPortAvailable(_udpPort))
            {
                Console.WriteLine($"\n[Ошибка] Порт {_tcpPort} или {_udpPort} уже занят другой программой.");
                Console.WriteLine("Нажмите любую клавишу для выхода...");
                Console.ReadKey(); 
                return;
            }

            TcpListener tcpListener = new TcpListener(serverIp, _tcpPort);
            try
            {
                tcpListener.Start();
                Console.WriteLine($"\n[Сервер запущен] Ожидание подключений на {serverIp}:{_tcpPort}");
                Console.WriteLine($"[UDP Уведомления] Будут рассылаться на порт {_udpPort}");

                while (true)
                {
                    TcpClient client = await tcpListener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[Критическая ошибка сервера] {ex.Message}");
                Console.ReadLine();
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
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"[TCP Сообщение от {clientEndPoint}]: {message}");

                    BroadcastTcpMessage(message, clientEndPoint);
                }
            }
            catch (Exception) { /* Игнорируем ошибки при обрыве связи */ }
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
                    catch { }
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

        private static IPAddress GetValidIpAddress(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string input = Console.ReadLine();
                if (IPAddress.TryParse(input, out IPAddress ip))
                {
                    return ip;
                }
                Console.WriteLine("[Ошибка] Неверный формат IP-адреса. Попробуйте еще раз.");
            }
        }

        private static int GetValidPort(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string input = Console.ReadLine();
                if (int.TryParse(input, out int port) && port > 0 && port <= 65535)
                {
                    return port;
                }
                Console.WriteLine("[Ошибка] Порт должен быть числом от 1 до 65535. Попробуйте еще раз.");
            }
        }
    }
}