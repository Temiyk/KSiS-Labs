using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace MyTraceroute
{
    class ProbeResult
    {
        public IPAddress Address { get; set; }
        public double RttMs { get; set; }
    }
    internal class Program
    {
        static int timeoutMs = 3000;
        static int maxJumps = 30;
        static int attemptsOnJump = 3;
        static ushort identifier;
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: SimpleTraceroute.exe <IP address or hostname>");
                return;
            }

            IPAddress target;
            try
            {
                var hostEntry = Dns.GetHostEntry(args[0]);
                target = hostEntry.AddressList[0];
                Console.WriteLine($"Tracing route to {args[0]} [{target}] with max {maxJumps} hops:");
            }
            catch {
                Console.WriteLine("Invalid hostname or IP address.");
                return;
            }

            identifier = (ushort)(Process.GetCurrentProcess().Id & 0xFFFF);

            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp))
            {
                socket.ReceiveTimeout = timeoutMs;
                socket.SendTimeout = timeoutMs;

                socket.Bind(new IPEndPoint(IPAddress.Any, 0));

                IPEndPoint targetEndPoint = new IPEndPoint(target, 0);
                byte[] receiveBuffer = new byte[1024];
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                int sequence = 1;
                for (int ttl = 1; ttl <= maxJumps; ttl++)
                {
                    Console.WriteLine($"{ttl,2}\t");
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, ttl);
                    List<ProbeResult> hopResults = new List<ProbeResult>();

                    for (int probe = 0; probe < attemptsOnJump; probe++)
                    {
                        byte[] data = new byte[32];
                        long sentTicks = DateTime.UtcNow.Ticks;
                        byte[] tickBytes = BitConverter.GetBytes(sentTicks);

                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(tickBytes);
                            Array.Copy(tickBytes, 0, data, 0, 8);
                        }

                        byte[] packet = BuildIcmpEchoRequest(identifier, (ushort)sequence, data);
                        DateTime sentTime = DateTime.UtcNow;
                        socket.SendTo(packet, targetEndPoint);
                        ProbeResult result = WaitForReply(socket, receiveBuffer, ref remoteEndPoint, identifier, (ushort)sequence, sentTime);

                        if (result != null)
                            hopResults.Add(result);
                        else
                            hopResults.Add(null);
                        sequence++;
                    }

                    foreach (var res in hopResults)
                    {
                        if (res != null)
                        {
                            Console.Write($"{res.RttMs,6:F0} ms\t");
                        }
                        else
                        {
                            Console.Write("   *\t");
                        }     
                    }

                    var addresses = hopResults.Where(r => r != null).Select(r => r.Address).Distinct().ToList();
                    if (addresses.Count > 0)
                    {
                        foreach (var addr in addresses)
                        {
                            Console.Write(addr + " ");
                        }  
                    }
                    else
                    {
                        Console.Write("***");
                    }
                    Console.WriteLine();
                    if (hopResults.Any(r => r != null && r.Address.Equals(target)))
                        break;
                }
            }
        }

        static byte[] BuildIcmpEchoRequest(ushort id, ushort seq, byte[] data)
        {
            byte[] packet = new byte[8 + data.Length];

            packet[0] = 8;
            packet[1] = 0;

            byte[] idBytes = BitConverter.GetBytes(id);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(idBytes);
            }
            Array.Copy(idBytes, 0, packet, 4, 2);

            byte[] seqBytes = BitConverter.GetBytes(seq);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(seqBytes);
            }
            Array.Copy(seqBytes, 0, packet, 6, 2);

            Array.Copy(data, 0, packet, 8, data.Length);
            ushort checksum = CalculateChecksum(packet);

            byte[] csBytes = BitConverter.GetBytes(checksum);
            if (BitConverter.IsLittleEndian) Array.Reverse(csBytes);
            packet[2] = csBytes[0];
            packet[3] = csBytes[1];

            return packet;
        }

        static ushort CalculateChecksum(byte[] buffer)
        {
            int length = buffer.Length;
            int sum = 0;
            int i = 0;

            while (length > 1)
            {
                sum += (buffer[i] << 8) | buffer[i + 1];
                i += 2;
                length -= 2;
            }

            if (length == 1)
            {
                sum += (buffer[i] << 8);
            }

            sum = (sum >> 16) + (sum & 0xFFFF);
            sum += (sum >> 16);

            return (ushort)(~sum);
        }

        static ProbeResult WaitForReply(Socket socket, byte[] buffer, ref EndPoint remoteEndPoint, ushort expectedId, ushort expectedSeq, DateTime sentTime)
        {
            try
            {
                int receivedBytes = socket.ReceiveFrom(buffer, ref remoteEndPoint);

                if (receivedBytes < 8)
                    return null;

                int ipHeaderLength = (buffer[0] & 0x0F) * 4;

                if (receivedBytes < ipHeaderLength + 8)
                    return null;

                int icmpStart = ipHeaderLength;

                byte type = buffer[icmpStart];

                if (type == 0)
                {
                    ushort recvId = (ushort)((buffer[icmpStart + 4] << 8) | buffer[icmpStart + 5]);
                    ushort recvSeq = (ushort)((buffer[icmpStart + 6] << 8) | buffer[icmpStart + 7]);

                    if (recvId == expectedId && recvSeq == expectedSeq)
                    {
                        double rtt = (DateTime.UtcNow - sentTime).TotalMilliseconds;
                        return new ProbeResult
                        {
                            Address = ((IPEndPoint)remoteEndPoint).Address,
                            RttMs = rtt
                        };
                    }
                }
                else if (type == 11)
                {
                    if (receivedBytes < 36)
                        return null;

                    int innerIpStart = icmpStart + 8;
                    int innerIpHeaderLen = (buffer[innerIpStart] & 0x0F) * 4;
                    int innerIcmpStart = innerIpStart + innerIpHeaderLen;

                    if (innerIcmpStart + 8 > receivedBytes)
                        return null;

                    ushort origId = (ushort)((buffer[innerIcmpStart + 4] << 8) | buffer[innerIcmpStart + 5]);
                    ushort origSeq = (ushort)((buffer[innerIcmpStart + 6] << 8) | buffer[innerIcmpStart + 7]);

                    if (origId == expectedId && origSeq == expectedSeq)
                    {
                        double rtt = (DateTime.UtcNow - sentTime).TotalMilliseconds;
                        return new ProbeResult
                        {
                            Address = ((IPEndPoint)remoteEndPoint).Address,
                            RttMs = rtt
                        };
                    }
                }
            }
            catch (SocketException)
            {
                return null;
            }

            return null;
        }
    }
}
