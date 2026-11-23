using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace GlassSidebar
{
    internal sealed class Settings
    {
        public string Provider = "anthropic";   // "anthropic" or "openai"
        public string Model = "claude-sonnet-5-5";
        public string BaseUrl = "https://api.openai.com/v1";
        public string EncryptedKey = "";
        public int Sensitivity = 1;              // 0 low, 1 medium, 2 high
        // glass appearance (applied live)
        public int TintR = 92, TintG = 94, TintB = 100;     // semi-gray
        public double TintStrength = 0.42;                  // 0 = clear, 0.85 = nearly solid
        public double Blur = 9;                              // px at 100% scale
        public string SystemPrompt = "You are a fast, concise assistant living in a sidebar. Keep answers tight.";

        [ScriptIgnore] static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlassSidebar"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        // Key is encrypted with Windows DPAPI: only your Windows account can decrypt it.
        [ScriptIgnore]
        public string ApiKey
        {
            get
            {
                if (string.IsNullOrEmpty(EncryptedKey)) return "";
                try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(EncryptedKey), null, DataProtectionScope.CurrentUser)); }
                catch { return ""; }
            }
            set
            {
                EncryptedKey = string.IsNullOrEmpty(value) ? "" :
                    Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
            }
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this));
        }

        // Pulls a key out of pasted text, a .txt, a .env, or a .json file.
        public static string ExtractKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = Regex.Match(text, @"sk-ant-[A-Za-z0-9_\-]{20,}");
            if (m.Success) return m.Value;
            m = Regex.Match(text, @"AIza[0-9A-Za-z_\-]{30,}");   // Google / Gemini keys
            if (m.Success) return m.Value;
            m = Regex.Match(text, @"gsk_[0-9A-Za-z]{20,}");      // Groq keys
            if (m.Success) return m.Value;
            m = Regex.Match(text, @"(?:ANTHROPIC|OPENAI|OPENROUTER|GROQ|GEMINI|GOOGLE)?_?API_KEY\s*[=:]\s*[""']?([A-Za-z0-9_\-\.]{16,})");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(text, @"sk-[A-Za-z0-9_\-]{20,}");
            if (m.Success) return m.Value;
            string t = text.Trim();
            if (t.Length >= 16 && !t.Contains(" ") && !t.Contains("\n")) return t;
            return null;
        }
    }

    internal sealed class ChatMessage
    {
        public string Role; public string Content;
        public ChatMessage() { }   // for JSON loading
        public ChatMessage(string role, string content) { Role = role; Content = content; }
    }

    internal static class AiClient
    {
        static readonly HttpClient http;

        static AiClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(5);
        }

        public const string GroqBase = "https://api.groq.com/openai/v1";

        // Every chat model on this Groq account (skips speech-to-text, TTS and safety-guard models).
        public static async Task<List<KeyValuePair<string, string>>> ListGroqModelsAsync(string key)
        {
            var result = new List<KeyValuePair<string, string>>();
            var req = new HttpRequestMessage(HttpMethod.Get, GroqBase + "/models");
            req.Headers.Add("Authorization", "Bearer " + key);
            using (var resp = await http.SendAsync(req))
            {
                string raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) throw new Exception("Couldn't list models (" + (int)resp.StatusCode + "): " + ErrorText(raw));
                var page = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw);
                object data;
                if (page.TryGetValue("data", out data))
                    foreach (var o in (System.Collections.IEnumerable)data)
                    {
                        var mdl = o as Dictionary<string, object>;
                        if (mdl == null) continue;
                        string id = mdl["id"] as string ?? "";
                        object active;
                        if (mdl.TryGetValue("active", out active) && active is bool && !(bool)active) continue;
                        string low = id.ToLowerInvariant();
                        if (low.Contains("whisper") || low.Contains("tts") || low.Contains("guard") || low.Contains("orpheus")) continue;
                        result.Add(new KeyValuePair<string, string>(id, id));
                    }
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return result;
        }

        public const string GeminiOpenAiBase = "https://generativelanguage.googleapis.com/v1beta/openai";
        public const string GeminiDefaultModel = "gemini-4-hydrogen";

        // Every Gemini model this key can chat with, straight from Google (so new models show up automatically).
        public static async Task<List<KeyValuePair<string, string>>> ListGeminiModelsAsync(string key)
        {
            var result = new List<KeyValuePair<string, string>>();
            var json = new JavaScriptSerializer();
            string pageToken = null;
            do
            {
                string url = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000" +
                             (pageToken != null ? "&pageToken=" + Uri.EscapeDataString(pageToken) : "");
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("x-goog-api-key", key);
                using (var resp = await http.SendAsync(req))
                {
                    string raw = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode) throw new Exception("Couldn't list models (" + (int)resp.StatusCode + "): " + ErrorText(raw));
                    var page = json.Deserialize<Dictionary<string, object>>(raw);
                    object models;
                    if (page.TryGetValue("models", out models))
                        foreach (var o in (System.Collections.IEnumerable)models)
                        {
                            var mdl = o as Dictionary<string, object>;
                            if (mdl == null) continue;
                            string id = (mdl["name"] as string ?? "").Replace("models/", "");
                            object methods, display;
                            bool chat = mdl.TryGetValue("supportedGenerationMethods", out methods) &&
                                        ((System.Collections.IEnumerable)methods).Cast<object>().Contains("generateContent");
                            if (!chat || !id.StartsWith("gemini")) continue;
                            mdl.TryGetValue("displayName", out display);
                            result.Add(new KeyValuePair<string, string>(id, (display as string) ?? id));
                        }
                    object next;
                    pageToken = page.TryGetValue("nextPageToken", out next) ? next as string : null;
                    if (pageToken == "") pageToken = null;
                }
            } while (pageToken != null);
            // newest-looking first: higher version numbers sort to the top
            result.Sort((a, b) => string.CompareOrdinal(b.Key, a.Key));
            return result;
        }

        public static async Task StreamAsync(Settings s, List<ChatMessage> history, Action<string> onDelta, CancellationToken ct)
        {
            string key = s.ApiKey;
            if (string.IsNullOrEmpty(key)) throw new Exception("No API key yet. Tap the key button to add one.");
            var json = new JavaScriptSerializer();
            HttpRequestMessage req;
            bool anthropic = s.Provider == "anthropic";

            var msgs = new List<Dictionary<string, object>>();
            if (!anthropic && !string.IsNullOrEmpty(s.SystemPrompt))
                msgs.Add(new Dictionary<string, object> { { "role", "system" }, { "content", s.SystemPrompt } });
            foreach (var m in history)
                msgs.Add(new Dictionary<string, object> { { "role", m.Role }, { "content", m.Content } });

            if (anthropic)
            {
                var body = new Dictionary<string, object> {
                    { "model", s.Model }, { "max_tokens", 4096 }, { "stream", true }, { "messages", msgs } };
                if (!string.IsNullOrEmpty(s.SystemPrompt)) body["system"] = s.SystemPrompt;
                req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                req.Headers.Add("x-api-key", key);
                req.Headers.Add("anthropic-version", "2023-06-01");
                req.Content = new StringContent(json.Serialize(body), Encoding.UTF8, "application/json");
            }
            else
            {
                // OpenAI-compatible; Gemini exposes the same chat/completions shape
                string baseUrl = s.Provider == "gemini" ? GeminiOpenAiBase : (s.Provider == "groq" ? GroqBase : s.BaseUrl);
                var body = new Dictionary<string, object> { { "model", s.Model }, { "stream", true }, { "messages", msgs } };
                req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions");
                req.Headers.Add("Authorization", "Bearer " + key);
                req.Content = new StringContent(json.Serialize(body), Encoding.UTF8, "application/json");
            }

            using (var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync();
                    throw new Exception("API error " + (int)resp.StatusCode + ": " + ErrorText(err));
                }
                using (var stream = await resp.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    while (!reader.EndOfStream)
                    {
                        ct.ThrowIfCancellationRequested();
                        string line = await reader.ReadLineAsync();
                        if (line == null) break;
                        if (!line.StartsWith("data:")) continue;
                        string data = line.Substring(5).Trim();
                        if (data == "[DONE]") break;
                        Dictionary<string, object> evt;
                        try { evt = json.Deserialize<Dictionary<string, object>>(data); } catch { continue; }
                        string delta = anthropic ? AnthropicDelta(evt) : OpenAiDelta(evt);
                        if (!string.IsNullOrEmpty(delta)) onDelta(delta);
                    }
                }
            }
        }

        static string AnthropicDelta(Dictionary<string, object> e)
        {
            object type; e.TryGetValue("type", out type);
            if ((type as string) == "error") throw new Exception(ErrorText(new JavaScriptSerializer().Serialize(e)));
            if ((type as string) != "content_block_delta") return null;
            var d = e["delta"] as Dictionary<string, object>;
            object text;
            return d != null && d.TryGetValue("text", out text) ? text as string : null;
        }

        static string OpenAiDelta(Dictionary<string, object> e)
        {
            object choices;
            if (!e.TryGetValue("choices", out choices)) return null;
            var arr = choices as object[] ?? (choices as System.Collections.ArrayList ?? new System.Collections.ArrayList()).ToArray();
            if (arr.Length == 0) return null;
            var c = arr[0] as Dictionary<string, object>;
            object delta;
            if (c == null || !c.TryGetValue("delta", out delta)) return null;
            var d = delta as Dictionary<string, object>;
            object content;
            return d != null && d.TryGetValue("content", out content) ? content as string : null;
        }

        static string ErrorText(string raw)
        {
            var m = Regex.Match(raw ?? "", "\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : (raw ?? "").Length > 300 ? raw.Substring(0, 300) : raw;
        }
    }
}
