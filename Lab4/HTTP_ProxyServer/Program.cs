using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SimpleHttpProxy
{
    class Program
    {
        static async Task Main(string[] args)
        {
            IPAddress ipAddress;
            while (true)
            {
                Console.Write("Введите IP-адрес для прослушивания (нажмите Enter для 127.0.0.1): ");
                string ipInput = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(ipInput))
                {
                    ipAddress = IPAddress.Parse("127.0.0.1");
                    break;
                }

                if (IPAddress.TryParse(ipInput, out ipAddress))
                {
                    break;
                }

                Console.WriteLine("Некорректный IP-адрес. Пожалуйста, попробуйте снова.");
            }

            int port;
            while (true)
            {
                Console.Write("Введите порт (нажмите Enter для 8888): ");
                string portInput = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(portInput))
                {
                    port = 8888;
                    break;
                }

                if (int.TryParse(portInput, out port) && port > 0 && port <= 65535)
                {
                    break;
                }

                Console.WriteLine("Некорректный порт. Введите числовое значение от 1 до 65535.");
            }

            TcpListener listener;
            try
            {
                listener = new TcpListener(ipAddress, port);
                listener.Start();
                Console.WriteLine($"\nПрокси-сервер успешно запущен на {ipAddress}:{port}. Ожидание подключений...\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось запустить сервер: {ex.Message}");
                return; 
            }

            while (true)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при принятии нового подключения: {ex.Message}");
                }
            }
        }

        static async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream clientStream = client.GetStream();

                    byte[] buffer = new byte[8192];
                    int bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesRead == 0) return;

                    string requestHeader = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                    int firstNewLine = requestHeader.IndexOf("\r\n");
                    if (firstNewLine == -1) return;

                    string firstLine = requestHeader.Substring(0, firstNewLine);
                    string[] requestParts = firstLine.Split(' ');

                    if (requestParts.Length < 3) return;

                    string method = requestParts[0];
                    string url = requestParts[1];
                    string version = requestParts[2];

                    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                    {
                        return;
                    }

                    string newFirstLine = $"{method} {uri.PathAndQuery} {version}\r\n";
                    byte[] newFirstLineBytes = Encoding.ASCII.GetBytes(newFirstLine);

                    using (TcpClient server = new TcpClient())
                    {
                        await server.ConnectAsync(uri.Host, uri.Port);

                        using (NetworkStream serverStream = server.GetStream())
                        {
                            await serverStream.WriteAsync(newFirstLineBytes, 0, newFirstLineBytes.Length);

                            int remainingLength = bytesRead - (firstNewLine + 2);
                            if (remainingLength > 0)
                            {
                                await serverStream.WriteAsync(buffer, firstNewLine + 2, remainingLength);
                            }

                            Task clientToServerTask = RelayDataAsync(clientStream, serverStream);

                            byte[] respBuffer = new byte[8192];
                            int respBytesRead = await serverStream.ReadAsync(respBuffer, 0, respBuffer.Length);

                            if (respBytesRead > 0)
                            {
                                string responseHeader = Encoding.ASCII.GetString(respBuffer, 0, respBytesRead);
                                int respFirstNewLine = responseHeader.IndexOf("\r\n");

                                if (respFirstNewLine != -1)
                                {
                                    string statusLine = responseHeader.Substring(0, respFirstNewLine);
                                    string[] statusParts = statusLine.Split(new char[] { ' ' }, 3);

                                    if (statusParts.Length >= 2)
                                    {
                                        string statusCode = statusParts[1];
                                        string statusText = statusParts.Length == 3 ? statusParts[2] : "";

                                        Console.WriteLine($"{url} - {statusCode} {statusText}");
                                    }
                                }

                                await clientStream.WriteAsync(respBuffer, 0, respBytesRead);
                            }

                            Task serverToClientTask = RelayDataAsync(serverStream, clientStream);

                            await Task.WhenAll(clientToServerTask, serverToClientTask);
                        } 
                    } 
                }
                catch (Exception)
                {
                }
            }
        }

        static async Task RelayDataAsync(NetworkStream input, NetworkStream output)
        {
            byte[] buffer = new byte[8192];
            int bytesRead;
            try
            {
                while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await output.WriteAsync(buffer, 0, bytesRead);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}