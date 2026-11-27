using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Oopz.Native
{
    internal sealed class LiveChannel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Area { get; set; }
    }

    internal sealed class LiveMessage
    {
        public string ChannelId { get; set; }
        public string Id { get; set; }
        public string Author { get; set; }
        public string Text { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
    }

    // OneBot v11 channel IDs are integers maintained by the local adapter.
    // They are distinct from the channel IDs in responses from the Oopz website.
    internal sealed class AdapterSession : IDisposable
    {
        private const int MaxResponseBytes = 4 * 1024 * 1024;
        private const int MaxEventBytes = 256 * 1024;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly HttpClient http;
        private readonly Uri eventsUri;
        private readonly string authorization;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken closed;
        private int disposed;

        public AdapterSession(int port, string token)
        {
            if (port < 1 || port > 65535)
                throw new ArgumentOutOfRangeException("port", "端口必须在 1 到 65535 之间。");
            if (!string.IsNullOrEmpty(token))
            {
                foreach (char c in token)
                    if (c <= ' ' || c >= 127)
                        throw new ArgumentException("适配器令牌包含空格或无效字符。");
                authorization = "Bearer " + token;
            }
            closed = lifetime.Token;
            var address = new UriBuilder("http", "127.0.0.1", port);
            eventsUri = new UriBuilder("ws", address.Host, port, "/event").Uri;
            var handler = new HttpClientHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                UseCookies = false
            };
            http = new HttpClient(handler)
            {
                BaseAddress = address.Uri,
                Timeout = RequestTimeout,
                MaxResponseContentBufferSize = MaxResponseBytes
            };
            if (authorization != null)
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        public async Task<string> GetAccountAsync(CancellationToken cancellation)
        {
            var data = (await CallAsync("get_login_info", new { }, cancellation).ConfigureAwait(false))
                as IDictionary<string, object>;
            if (data == null) throw InvalidResponse();
            return Field(data, "nickname") ?? Field(data, "user_id") ?? "当前账号";
        }

        public async Task<List<LiveChannel>> GetChannelsAsync(CancellationToken cancellation)
        {
            var data = (await CallAsync("get_group_list", new { }, cancellation).ConfigureAwait(false)) as object[];
            if (data == null) throw InvalidResponse();
            var channels = new List<LiveChannel>();
            var seen = new HashSet<string>();
            foreach (var item in data)
            {
                var channel = item as IDictionary<string, object>;
                string id = NumericId(channel, "group_id");
                if (id == null || !seen.Add(id)) continue;
                var extra = Get(channel, "extra") as IDictionary<string, object>;
                channels.Add(new LiveChannel
                {
                    Id = id,
                    Name = Field(channel, "group_name") ?? id,
                    Area = Field(extra, "oopz_area_id") ?? ""
                });
            }
            return channels;
        }

        public async Task<List<LiveMessage>> GetRecentAsync(CancellationToken cancellation)
        {
            var data = (await CallAsync("get_latest_events", new { limit = 100 }, cancellation)
                .ConfigureAwait(false)) as object[];
            if (data == null) throw InvalidResponse();
            var result = new List<LiveMessage>();
            foreach (var item in data)
            {
                var message = ReadEvent(item);
                if (message != null) result.Add(message);
            }
            return result;
        }

        public async Task<string> SendAsync(string channelId, string text, CancellationToken cancellation)
        {
            long id;
            if (!long.TryParse(channelId, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                throw new ArgumentException("请选择适配器中的文字频道。");
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("请输入消息。");
            var result = await CallAsync("send_group_msg",
                new { group_id = id, message = text, auto_escape = true }, cancellation).ConfigureAwait(false);
            string messageId = NumericId(result as IDictionary<string, object>, "message_id");
            if (messageId == null) throw new IOException("适配器未返回消息编号，请先确认频道中是否已发送。");
            return messageId;
        }

        private async Task<object> CallAsync(string action, object parameters, CancellationToken cancellation)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(closed, cancellation))
            using (var body = new StringContent(Json().Serialize(parameters), Encoding.UTF8, "application/json"))
            {
                try
                {
                    // ResponseContentRead (PostAsync's default) applies both Timeout and
                    // MaxResponseContentBufferSize through the end of the response body.
                    using (var response = await http.PostAsync(action, body, linked.Token).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                            throw new IOException("适配器拒绝连接，请检查适配器令牌。");
                        if (!response.IsSuccessStatusCode)
                            throw new IOException("适配器返回 HTTP " + (int)response.StatusCode + "（" + action + "）。");
                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var data = Parse(json) as IDictionary<string, object>;
                        if (data == null || !data.ContainsKey("retcode")) throw InvalidResponse();
                        if (Field(data, "status") != "ok" || Field(data, "retcode") != "0")
                            throw new IOException("适配器操作失败（" + action + "，返回码 " + Field(data, "retcode") + "）。");
                        return Get(data, "data");
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!linked.IsCancellationRequested)
                        throw new IOException("适配器响应超时（" + action + "）。");
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    throw new IOException("无法完成本机适配器请求（" + action + "），请检查服务、端口和响应大小。", ex);
                }
            }
        }

        // .NET Framework ClientWebSocket builds an HttpWebRequest but exposes no
        // redirect switch. Keep its normal WebSocket implementation and override
        // just that flag for the exact loopback endpoint before connecting.
        private sealed class LoopbackHandshake : IWebRequestCreate
        {
            public static readonly LoopbackHandshake Instance = new LoopbackHandshake();
            public WebRequest Create(Uri uri)
            {
                var request = (HttpWebRequest)WebRequest.CreateDefault(uri);
                request.AllowAutoRedirect = false;
                return request;
            }
        }

        public async Task ListenAsync(Action<LiveMessage> received, CancellationToken cancellation, Action connected = null)
        {
            if (received == null) throw new ArgumentNullException("received");
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(closed, cancellation))
            using (var socket = new ClientWebSocket())
            {
                WebRequest.RegisterPrefix(eventsUri.AbsoluteUri, LoopbackHandshake.Instance);
                socket.Options.Proxy = null;
                if (authorization != null) socket.Options.SetRequestHeader("Authorization", authorization);
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token))
                {
                    deadline.CancelAfter(RequestTimeout);
                    try
                    {
                        await socket.ConnectAsync(eventsUri, deadline.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        throw new IOException("连接本机实时事件时超时。");
                    }
                    catch (WebSocketException ex)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        throw new IOException("无法连接本机实时事件，请检查端口、令牌和适配器的正向 WebSocket 设置。", ex);
                    }
                }
                if (connected != null) connected();
                var chunk = new byte[8192];
                using (var buffer = new MemoryStream())
                {
                    while (true)
                    {
                        var frame = await socket.ReceiveAsync(new ArraySegment<byte>(chunk), linked.Token)
                            .ConfigureAwait(false);
                        if (frame.MessageType == WebSocketMessageType.Close)
                        {
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", linked.Token)
                                .ConfigureAwait(false);
                            throw new IOException("本机适配器已关闭实时连接。");
                        }
                        if (frame.MessageType != WebSocketMessageType.Text)
                            throw new IOException("适配器返回了无法识别的事件格式。");
                        if (buffer.Length + frame.Count > MaxEventBytes)
                            throw new IOException("适配器返回的单个事件过大。");
                        buffer.Write(chunk, 0, frame.Count);
                        if (!frame.EndOfMessage) continue;
                        var message = ReadEvent(Parse(StrictUtf8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length)));
                        buffer.SetLength(0);
                        if (message != null) received(message);
                    }
                }
            }
        }

        private static LiveMessage ReadEvent(object value)
        {
            var data = value as IDictionary<string, object>;
            if (Field(data, "post_type") != "message" || Field(data, "message_type") != "group")
                return null;
            string channelId = NumericId(data, "group_id");
            string messageId = NumericId(data, "message_id");
            if (channelId == null || messageId == null) return null;
            var segments = Get(data, "message") as object[];
            var text = new StringBuilder();
            if (segments != null)
            {
                foreach (var item in segments)
                {
                    var segment = item as IDictionary<string, object>;
                    var content = Get(segment, "data") as IDictionary<string, object>;
                    switch (Field(segment, "type"))
                    {
                        case "text": text.Append(Field(content, "text")); break;
                        case "at":
                            var person = Field(content, "qq");
                            text.Append(person == "all" ? "@所有人" : "@" + person);
                            break;
                        case "image": text.Append("[图片]"); break;
                        default: text.Append("[附件]"); break;
                    }
                }
            }
            var sender = Get(data, "sender") as IDictionary<string, object>;
            DateTimeOffset? time = null;
            long seconds;
            if (long.TryParse(Field(data, "time"), out seconds)
                && seconds >= -62135596800L && seconds <= 253402300799L)
                time = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return new LiveMessage
            {
                ChannelId = channelId,
                Id = messageId,
                Author = Field(sender, "nickname") ?? Field(data, "user_id") ?? "未知用户",
                Text = text.Length > 0 ? text.ToString() : Field(data, "raw_message") ?? Field(data, "message") ?? "",
                Timestamp = time
            };
        }

        private static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxResponseBytes, RecursionLimit = 64 };
        }

        private static object Parse(string text)
        {
            try { return Json().DeserializeObject(text); }
            catch (ArgumentException) { throw InvalidResponse(); }
            catch (InvalidOperationException) { throw InvalidResponse(); }
        }

        private static IOException InvalidResponse()
        {
            return new IOException("适配器的响应格式与 OneBot v11 不兼容。");
        }

        private static object Get(IDictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value : null;
        }

        private static string Field(IDictionary<string, object> data, string key)
        {
            var value = Get(data, key);
            if (value is string) return string.IsNullOrEmpty((string)value) ? null : (string)value;
            return value is int || value is long || value is decimal
                ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        private static string NumericId(IDictionary<string, object> data, string key)
        {
            long id;
            return long.TryParse(Field(data, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
                ? id.ToString(CultureInfo.InvariantCulture) : null;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lifetime.Cancel();
            http.Dispose();
            lifetime.Dispose();
        }
    }
}
