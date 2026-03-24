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
            if (string.IsNullOrWhiteSpace(userName)) userName = "Аноним";

            IPAddress localIp = GetValidIpAddress("Введите ваш ЛОКАЛЬНЫЙ IP-адрес (пример: 127.0.0.2): ");
            int localTcpPort = GetValidPort("Введите ваш ЛОКАЛЬНЫЙ TCP-порт (или 0 для автоматического выбора): ", true);
            int localUdpPort = GetValidPort("Введите UDP-порт для получения уведомлений (должен совпадать с UDP сервера): ");

            IPAddress serverIp = GetValidIpAddress("Введите IP-адрес СЕРВЕРА (пример: 127.0.0.1): ");

            if (localIp.Equals(serverIp))
            {
                Console.WriteLine("\n[Ошибка] Ваш локальный IP-адрес не может совпадать с IP-адресом сервера!");
                Console.WriteLine("Подсказка: Для тестирования на одном ПК используйте разные адреса.");
                Console.WriteLine("Например: Сервер -> 127.0.0.1, Клиент -> 127.0.0.2");
                Console.WriteLine("\nНажмите Enter для выхода...");
                Console.ReadLine();
                return; 
            }

            int serverPort = GetValidPort("Введите TCP-порт СЕРВЕРА: ");

            TcpClient tcpClient = null;
            UdpClient udpClient = null;

            try
            {
                IPEndPoint localEndPoint = new IPEndPoint(localIp, localTcpPort);
                tcpClient = new TcpClient(localEndPoint);

                Console.WriteLine("\nПодключение к серверу...");
                await tcpClient.ConnectAsync(serverIp, serverPort);
                Console.WriteLine("Успешно подключено!\n");

                udpClient = new UdpClient(new IPEndPoint(localIp, localUdpPort));

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
            catch (SocketException ex)
            {
                Console.WriteLine($"\n[Ошибка Сети] Не удалось установить соединение или привязать порт.");
                Console.WriteLine($"Детали: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[Непредвиденная ошибка]: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("\nНажмите любую клавишу для завершения работы...");
                Console.ReadKey();
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
            catch { /* Игнорируем ошибки при закрытии */ }
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

        private static int GetValidPort(string prompt, bool allowZero = false)
        {
            while (true)
            {
                Console.Write(prompt);
                string input = Console.ReadLine();
                if (int.TryParse(input, out int port))
                {
                    if (allowZero && port == 0) return port;

                    if (port > 0 && port <= 65535) return port;
                }
                Console.WriteLine("[Ошибка] Порт должен быть числом от 1 до 65535. Попробуйте еще раз.");
            }
        }
    }
}