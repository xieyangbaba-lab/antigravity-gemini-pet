using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AntigravityGeminiPet
{
    internal static class PetCompanionHost
    {
        private const int UdpPort = 47291;

        private static int Main(string[] args)
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);

            int myPid = Process.GetCurrentProcess().Id;
            string exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            string petExe = Path.Combine(exeDir, "GeminiPet.exe");

            // Launch or attach to GeminiPet cleanly (UseShellExecute=true avoids inheriting MCP stdio pipes!)
            LaunchOrAttachPet(petExe, myPid);

            try
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;

                    string method = ExtractJsonString(line, "method");
                    string rawId = ExtractJsonRawId(line);

                    // Notifications have no "id" -> do not reply
                    if (string.IsNullOrEmpty(rawId)) continue;

                    if (method == "initialize")
                    {
                        string proto = ExtractJsonString(line, "protocolVersion");
                        if (string.IsNullOrEmpty(proto)) proto = "2024-11-05";
                        WriteJson("{\"jsonrpc\":\"2.0\",\"id\":" + rawId +
                                  ",\"result\":{\"protocolVersion\":\"" + EscapeJson(proto) +
                                  "\",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"gemini-pet-companion\",\"version\":\"1.0.0\"}}}");
                    }
                    else if (method == "tools/list")
                    {
                        WriteJson("{\"jsonrpc\":\"2.0\",\"id\":" + rawId + ",\"result\":{\"tools\":[]}}");
                    }
                    else if (method == "resources/list")
                    {
                        WriteJson("{\"jsonrpc\":\"2.0\",\"id\":" + rawId + ",\"result\":{\"resources\":[]}}");
                    }
                    else if (method == "prompts/list")
                    {
                        WriteJson("{\"jsonrpc\":\"2.0\",\"id\":" + rawId + ",\"result\":{\"prompts\":[]}}");
                    }
                    else if (method == "ping")
                    {
                        WriteJson("{\"jsonrpc\":\"2.0\",\"id\":" + rawId + ",\"result\":{}}");
                    }
                    else
                    {
                        WriteJson("{\"jsonrpc\":\"2.0\",\"id\":" + rawId +
                                  ",\"error\":{\"code\":-32601,\"message\":\"Method not found\"}}");
                    }
                }
            }
            catch
            {
            }
            finally
            {
                // Notify GeminiPet immediately that this Antigravity host has closed!
                SendUdp("detach_host|" + myPid);
            }
            return 0;
        }

        private static void LaunchOrAttachPet(string petExe, int myPid)
        {
            try
            {
                Process[] existing = Process.GetProcessesByName("GeminiPet");
                if (existing != null && existing.Length > 0)
                {
                    SendUdp("attach_host|" + myPid);
                    // Send a 750ms confirmation ping in case GeminiPet was still starting its UDP listener
                    Thread retry = new Thread(delegate()
                    {
                        try { Thread.Sleep(750); SendUdp("attach_host|" + myPid); }
                        catch { }
                    });
                    retry.IsBackground = true;
                    retry.Start();
                }
                else if (File.Exists(petExe))
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = petExe;
                    psi.Arguments = "--host-pid " + myPid;
                    psi.WorkingDirectory = Path.GetDirectoryName(petExe);
                    psi.UseShellExecute = true; // Critical: do NOT inherit Antigravity stdio pipe handles!
                    Process.Start(psi);
                }
            }
            catch
            {
            }
        }

        private static void SendUdp(string payload)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(payload);
                using (UdpClient udp = new UdpClient())
                {
                    udp.Send(data, data.Length, "127.0.0.1", UdpPort);
                }
            }
            catch
            {
            }
        }

        private static void WriteJson(string json)
        {
            Console.Out.WriteLine(json);
            Console.Out.Flush();
        }

        private static string ExtractJsonString(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static string ExtractJsonRawId(string json)
        {
            Match m = Regex.Match(json, "\"id\"\\s*:\\s*(\"[^\"]*\"|-?\\d+)");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
