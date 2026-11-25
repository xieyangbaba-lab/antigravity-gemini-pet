using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace AntigravityGeminiPet
{
    internal static class PetNotify
    {
        private const int UdpPort = 47291;

        private static int Main(string[] args)
        {
            string eventName = args.Length > 0 ? args[0] : "idle";
            bool isHookEvent = (eventName == "session_start" || eventName == "pre_invocation" || eventName == "post_tool" || eventName == "stop");
            string stdinText = "";
            try
            {
                if (isHookEvent && Console.IsInputRedirected)
                {
                    stdinText = Console.In.ReadToEnd();
                }
            }
            catch
            {
            }

            string toolName = ExtractJsonString(stdinText, "name");
            string errorText = ExtractJsonString(stdinText, "error");
            string termReason = ExtractJsonString(stdinText, "terminationReason");

            string state = "idle";
            string msg = "";

            if (eventName == "session_start")
            {
                state = "wave";
                msg = "\u4e3b\u4eba\u6765\u5566\uff01Gemini \u732b\u5a18\u968f\u65f6\u542c\u5019\u5dee\u9063\u55b5 \u2726";
            }
            else if (eventName == "pre_invocation")
            {
                state = "thinking";
                msg = "\u6536\u5230\u4e3b\u4eba\u6307\u4ee4\uff0c\u6b63\u5728\u8ba4\u771f\u601d\u8003\u4e2d\u55b5...";
            }
            else if (eventName == "post_tool")
            {
                if (!string.IsNullOrEmpty(errorText))
                {
                    state = "thinking";
                    msg = "\u54ce\u5440\u4e3b\u4eba\uff0c\u521a\u624d\u6709\u70b9\u5c0f\u72b6\u51b5\uff0c\u6211\u518d\u770b\u770b\u55b5...";
                }
                else
                {
                    state = "working";
                    msg = MapToolMessage(toolName);
                }
            }
            else if (eventName == "stop")
            {
                if (termReason == "ERROR")
                {
                    state = "thinking";
                    msg = "\u4e3b\u4eba\uff0c\u521a\u624d\u51fa\u4e86\u70b9\u5c0f\u95ee\u9898\uff0c\u9700\u8981\u5e2e\u5fd9\u770b\u4e00\u4e0b\u55b5";
                }
                else
                {
                    state = "happy";
                    msg = "\u641e\u5b9a\u5566\u4e3b\u4eba\uff01\u5feb\u6765\u770b\u770b\u7ed3\u679c\u5427 \u2726";
                }
            }
            else
            {
                state = eventName;
                if (args.Length > 1)
                {
                    msg = args[1];
                }
            }

            try
            {
                string payload = state + "|" + msg;
                byte[] data = Encoding.UTF8.GetBytes(payload);
                using (UdpClient udp = new UdpClient())
                {
                    udp.Send(data, data.Length, "127.0.0.1", UdpPort);
                }
            }
            catch
            {
            }

            // Always return valid empty JSON object for Antigravity Hooks contract
            Console.WriteLine("{}");
            return 0;
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return "";
            try
            {
                Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
                if (m.Success) return m.Groups[1].Value;
            }
            catch
            {
            }
            return "";
        }

        private static string MapToolMessage(string tool)
        {
            if (string.IsNullOrEmpty(tool)) return "\u6b63\u5728\u5e2e\u4e3b\u4eba\u65bd\u6cd5\u5e72\u6d3b\u4e2d\u55b5 \u2726";
            string t = tool.ToLowerInvariant();
            if (t.Contains("run_command") || t.Contains("command"))
                return "\u6b63\u5728\u5e2e\u4e3b\u4eba\u6267\u884c\u7ec8\u7aef\u547d\u4ee4\u55b5 \u2726";
            if (t.Contains("write") || t.Contains("edit"))
                return "\u6b63\u5728\u65bd\u6cd5\u5e2e\u4e3b\u4eba\u5199\u4ee3\u7801/\u6539\u6587\u4ef6\u55b5 \u2726";
            if (t.Contains("read") || t.Contains("view") || t.Contains("find") || t.Contains("grep"))
                return "\u6b63\u5728\u5e2e\u4e3b\u4eba\u7ffb\u9605\u4ee3\u7801\u8d44\u6599\u55b5 \u2726";
            if (t.Contains("search"))
                return "\u6b63\u5728\u5e2e\u4e3b\u4eba\u641c\u7d22\u8d44\u6599\u55b5 \u2726";
            if (t.Contains("browser"))
                return "\u6b63\u5728\u5e2e\u4e3b\u4eba\u64cd\u4f5c\u6d4f\u89c8\u5668\u55b5 \u2726";
            if (t.Contains("subagent"))
                return "\u53ec\u5524\u5206\u8eab\u4e00\u8d77\u5e2e\u4e3b\u4eba\u5e72\u6d3b\u55b5 \u2726";
            return "\u6b63\u5728\u5e2e\u4e3b\u4eba\u5904\u7406: " + tool + " \u2726";
        }
    }
}
