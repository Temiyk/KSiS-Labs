using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Lab3_Client
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("<<< КЛИЕНТ ЧАТА >>>");

            Console.Write("Введите ваш никнейм: ");
            string userName = Console.ReadLine();

            Console.Write("Введите ваш IP-адрес (пример: 127.0.0.1): ");
            IPAddress localIp = IPAddress.Parse(Console.ReadLine());

            Console.Write("Введите IP-адрес сервера (пример: 127.0.0.1): ");
            IPAddress serverIp = IPAddress.Parse(Console.ReadLine());

            Console.Write("Введите порт сервера: ");
            int serverPort = int.Parse(Console.ReadLine());

            int udpPort = serverPort + 1;

            TcpClient tcpClient = null;
            UdpClient udpClient = null;

            try
            {
                IPEndPoint localEndPoint = new IPEndPoint(localIp, 0);
                tcpClient = new TcpClient(localEndPoint);

                Console.WriteLine("Подключение к серверу...");
                await tcpClient.ConnectAsync(serverIp, serverPort);
                Console.WriteLine("Успешно подключено!\n");

                udpClient = new UdpClient(new IPEndPoint(localIp, udpPort));

                NetworkStream stream = tcpClient.GetStream();

                _ = Task.Run(() => ReceiveTcpMessages(stream));
                _ = Task.Run(() => ReceiveUdpNotifications(udpClient));

                Console.WriteLine("Вы можете писать сообщения. Для выхода закройте окно.\n");
                Console.WriteLine(new string('-', 40));

                while (true)
                {
                    string input = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(input)) continue;

                    
                    string fullMessage = $"{userName}: {input}";
                    byte[] data = Encoding.UTF8.GetBytes(fullMessage);

                    
                    await stream.WriteAsync(data, 0, data.Length);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[Ошибка] Сбой подключения: {ex.Message}");
            }
            finally
            {
                tcpClient?.Close();
                udpClient?.Close();
            }
        }

        private static async Task ReceiveTcpMessages(NetworkStream stream)
        {
            byte[] buffer = new byte[1024];
            try
            {
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        Console.WriteLine("\n[Система] Сервер закрыл соединение.");
                        break; 
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"\n{message}");
                }
            }
            catch
            {
                Console.WriteLine("\n[Система] Соединение с сервером потеряно.");
            }
        }

        private static async Task ReceiveUdpNotifications(UdpClient udpClient)
        {
            try
            {
                while (true)
                {
                    UdpReceiveResult result = await udpClient.ReceiveAsync();
                    string message = Encoding.UTF8.GetString(result.Buffer);
                    Console.WriteLine($"\n>>> [СЕРВЕРНОЕ УВЕДОМЛЕНИЕ]: {message} <<<");
                }
            }
            catch
            {
            }
        }
    }
}
