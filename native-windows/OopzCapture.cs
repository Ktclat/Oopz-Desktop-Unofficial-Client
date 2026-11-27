using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace Oopz.Native
{
    public sealed class OopzSnapshot
    {
        public List<OopzArea> Areas { get; private set; }
        internal OopzSnapshot() { Areas = new List<OopzArea>(); }
    }

    public sealed class OopzArea
    {
        public string Id { get; private set; }
        public string Name { get; internal set; }
        public List<OopzChannel> Channels { get; private set; }

        internal OopzArea(string id)
        {
            Id = Name = id;
            Channels = new List<OopzChannel>();
        }
    }

    public sealed class OopzChannel
    {
        public string Id { get; private set; }
        public string Name { get; internal set; }
        public string Type { get; internal set; }
        public string Group { get; internal set; }
        public List<OopzMessage> Messages { get; private set; }

        internal OopzChannel(string id)
        {
            Id = Name = id;
            Type = Group = "";
            Messages = new List<OopzMessage>();
        }
    }

    public sealed class OopzMessage
    {
        public string Id { get; internal set; }
        public string Author { get; internal set; }
        public string Text { get; internal set; }
        public DateTimeOffset? Timestamp { get; internal set; }
        internal bool Recalled { get; set; }
    }

    // Keep the capture transport separate from Oopz response models. The importer
    // reads local files and neither replays requests nor saves captured state.
    public sealed class OopzCapture
    {
        const int MaxFileSize = 32 * 1024 * 1024;
        const int MaxResponseSize = 2 * 1024 * 1024;
        const string AreasPath = "/userSubscribeArea/v1/list";
        const string ChannelsPath = "/client/v1/area/v1/detail/v1/channels";
        const string HistoryPath = "/im/session/v2/messageBefore";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly XmlDictionaryReaderQuotas HarQuotas = new XmlDictionaryReaderQuotas {
            MaxDepth = 64, MaxArrayLength = MaxFileSize, MaxStringContentLength = MaxFileSize,
            MaxNameTableCharCount = 65536
        };

        // Opt in only to HAR fields needed for routing or reading a response.
        [DataContract] sealed class Archive { [DataMember(Name = "log")] public Log Log; }
        [DataContract] sealed class Log { [DataMember(Name = "entries")] public IndexEntry[] Entries; }
        [DataContract] sealed class IndexEntry
        {
            [DataMember(Name = "request")] public Request Request;
            [DataMember(Name = "response")] public StatusOnly Response;
        }
        [DataContract] sealed class Request
        {
            [DataMember(Name = "method")] public string Method;
            [DataMember(Name = "url")] public string Url;
        }
        [DataContract] sealed class StatusOnly { [DataMember(Name = "status")] public int Status; }
        [DataContract] sealed class SelectedEntry { [DataMember(Name = "response")] public Response Response; }
        [DataContract] sealed class Response
        {
            [DataMember(Name = "content")] public Body Content;
        }
        [DataContract] sealed class Body
        {
            [DataMember(Name = "text")] public string Text;
            [DataMember(Name = "encoding")] public string Encoding;
        }

        readonly OopzSnapshot snapshot = new OopzSnapshot();
        readonly Dictionary<string, OopzArea> areas = new Dictionary<string, OopzArea>();
        readonly Dictionary<Tuple<string, string>, OopzChannel> channels = new Dictionary<Tuple<string, string>, OopzChannel>();
        readonly Dictionary<OopzChannel, Dictionary<string, OopzMessage>> history = new Dictionary<OopzChannel, Dictionary<string, OopzMessage>>();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaxResponseSize, RecursionLimit = 32 };

        private OopzCapture() { }

        public static OopzSnapshot Load(string harPath)
        {
            var reader = new OopzCapture();
            using (var stream = File.OpenRead(harPath))
            {
                if (stream.Length > MaxFileSize)
                    throw new InvalidDataException("HAR 文件超过 32 MiB，请只导出域、频道和消息请求。");
                try
                {
                    // Preselect by URL and HTTP status before materializing any response bodies.
                    // Other requests may contain credentials. HAR objects can put response first.
                    Archive index;
                    using (var indexStream = File.OpenRead(harPath))
                    using (var xml = JsonReaderWriterFactory.CreateJsonReader(indexStream, HarQuotas))
                        index = (Archive)new DataContractJsonSerializer(typeof(Archive)).ReadObject(xml);
                    if (index == null || index.Log == null || index.Log.Entries == null)
                        throw new InvalidDataException("文件中缺少 HAR 请求列表。");
                    var routes = index.Log.Entries.Select(Route).ToArray();
                    if (!reader.ReadResponses(stream, routes))
                        throw new InvalidDataException("没有找到带有成功响应正文的 Oopz 域、频道或消息请求。请导出包含内容的 HAR。");
                }
                catch (SerializationException) { throw new InvalidDataException("无法读取 HAR 的 JSON 结构。"); }
                catch (XmlException) { throw new InvalidDataException("无法读取 HAR 的 JSON 结构。"); }
            }

            foreach (var pair in reader.history)
                pair.Key.Messages.AddRange(pair.Value.Values.OrderBy(m => m.Timestamp).ThenBy(m => m.Id, StringComparer.Ordinal));
            return reader.snapshot;
        }

        static Uri Route(IndexEntry entry)
        {
            if (entry == null || entry.Request == null || entry.Request.Method != "GET" || entry.Response == null ||
                entry.Response.Status < 200 || entry.Response.Status >= 300) return null;
            Uri uri;
            if (!Uri.TryCreate(entry.Request.Url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0 || (uri.Host != "gateway-n.oopz.cn" &&
                uri.Host != "gateway.oopz.cn" && uri.Host != "api.oopz.cn")) return null;
            return uri.AbsolutePath == AreasPath || uri.AbsolutePath == ChannelsPath || uri.AbsolutePath == HistoryPath ? uri : null;
        }

        bool ReadResponses(Stream stream, Uri[] routes)
        {
            bool recognized = false;
            var serializer = new DataContractJsonSerializer(typeof(SelectedEntry));
            using (var xml = JsonReaderWriterFactory.CreateJsonReader(stream, HarQuotas))
            {
                xml.MoveToContent();
                if (!FindChild(xml, "log") || !FindChild(xml, "entries") || !FindChild(xml, "item")) return false;
                foreach (var uri in routes)
                {
                    if (uri == null) { xml.Skip(); continue; }
                    var entry = (SelectedEntry)serializer.ReadObject(xml, false);
                    if (entry != null && entry.Response != null && entry.Response.Content != null)
                        recognized |= ReadEntry(uri, entry.Response.Content);
                }
            }
            return recognized;
        }

        static bool FindChild(XmlReader reader, string name)
        {
            var depth = reader.Depth;
            reader.Read();
            while (reader.NodeType == XmlNodeType.Element && reader.Depth == depth + 1)
            {
                if (reader.LocalName == name) return true;
                reader.Skip();
            }
            return false;
        }

        bool ReadEntry(Uri uri, Body body)
        {
            var path = uri.AbsolutePath;
            object payload;
            if (!ReadPayload(body, out payload)) return false;
            var map = payload as Dictionary<string, object>;
            if (map != null)
            {
                object status;
                if (map.TryGetValue("status", out status) && !object.Equals(status, true)) return false;
                if (map.ContainsKey("code") && Scalar(map, "code") != "200") return false;
                if (map.ContainsKey("data")) payload = Value(map, "data");
            }

            if (path == AreasPath) return ReadAreas(payload);
            var areaId = Scope(uri, "area");
            if (areaId.Length == 0) return false;
            if (path == ChannelsPath) return ReadChannels(areaId, payload);
            var channelId = Scope(uri, "channel");
            return channelId.Length > 0 && ReadHistory(areaId, channelId, payload);
        }

        bool ReadPayload(Body body, out object payload)
        {
            payload = null;
            string text = body.Text;
            if (string.IsNullOrEmpty(text)) return false;
            if (string.Equals(body.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                if (text.Length > (MaxResponseSize + 2) / 3 * 4) return false;
                try
                {
                    var bytes = Convert.FromBase64String(text);
                    if (bytes.Length > MaxResponseSize) return false;
                    text = Utf8.GetString(bytes);
                }
                catch (FormatException) { return false; }
                catch (DecoderFallbackException) { return false; }
            }
            else if (!string.IsNullOrEmpty(body.Encoding) || text.Length > MaxResponseSize) return false;
            try { payload = json.DeserializeObject(text); }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            return payload != null;
        }

        bool ReadAreas(object payload)
        {
            var list = (payload as IList) ?? (Value(payload, "areas") as IList);
            if (list == null) return false;
            foreach (var item in list)
            {
                var id = Scalar(item, "areaId", "id");
                if (id.Length == 0) continue;
                var area = Area(id);
                var name = Scalar(item, "name", "areaName");
                if (name.Length > 0) area.Name = name;
            }
            return true;
        }

        bool ReadChannels(string areaId, object payload)
        {
            var groups = payload as IList;
            if (groups == null) return false;
            var area = Area(areaId);
            foreach (var group in groups)
            {
                var list = Value(group, "channels") as IList;
                if (list == null) continue;
                foreach (var item in list)
                {
                    var id = Scalar(item, "channelId", "id");
                    var ownArea = Scalar(item, "areaId", "area");
                    if (id.Length == 0 || (ownArea.Length > 0 && ownArea != areaId)) continue;
                    var channel = Channel(area, id);
                    var name = Scalar(item, "name");
                    if (name.Length > 0) channel.Name = name;
                    channel.Type = Scalar(item, "type");
                    channel.Group = Scalar(group, "name");
                }
            }
            return true;
        }

        bool ReadHistory(string areaId, string channelId, object payload)
        {
            var list = Value(payload, "messages") as IList;
            if (list == null) return false;
            var channel = Channel(Area(areaId), channelId);
            var messages = history[channel];
            foreach (var item in list)
            {
                var id = Scalar(item, "messageId", "id", "clientMessageId");
                var ownArea = Scalar(item, "area", "areaId");
                var ownChannel = Scalar(item, "channel", "channelId");
                if (id.Length == 0 || (ownArea.Length > 0 && ownArea != areaId) ||
                    (ownChannel.Length > 0 && ownChannel != channelId)) continue;

                OopzMessage previous;
                if (messages.TryGetValue(id, out previous) && previous.Recalled) continue;
                var recalled = object.Equals(Value(item, "recall"), true);
                var text = Scalar(item, "content", "text");
                if (recalled) text = "［消息已撤回］";
                else if (text.Length == 0) text = "［非文字消息］";
                var author = Scalar(item, "person", "senderUid", "senderId");
                var timestamp = Time(Scalar(item, "timestamp"));
                if (previous != null)
                {
                    if (author.Length == 0) author = previous.Author;
                    if (!timestamp.HasValue) timestamp = previous.Timestamp;
                }
                messages[id] = new OopzMessage {
                    Id = id, Author = author, Text = text, Recalled = recalled, Timestamp = timestamp
                };
            }
            return true;
        }

        OopzArea Area(string id)
        {
            OopzArea area;
            if (!areas.TryGetValue(id, out area))
            {
                area = areas[id] = new OopzArea(id);
                snapshot.Areas.Add(area);
            }
            return area;
        }

        OopzChannel Channel(OopzArea area, string id)
        {
            var key = Tuple.Create(area.Id, id);
            OopzChannel channel;
            if (!channels.TryGetValue(key, out channel))
            {
                channel = channels[key] = new OopzChannel(id);
                area.Channels.Add(channel);
                history[channel] = new Dictionary<string, OopzMessage>();
            }
            return channel;
        }

        static string Scope(Uri uri, string name)
        {
            string result = "";
            foreach (var pair in uri.Query.TrimStart('?').Split('&'))
            {
                var equals = pair.IndexOf('=');
                if (equals < 0 || pair.Substring(0, equals) != name) continue;
                if (result.Length > 0) return "";
                result = Uri.UnescapeDataString(pair.Substring(equals + 1).Replace('+', ' '));
            }
            return result;
        }

        static object Value(object item, string key)
        {
            var map = item as Dictionary<string, object>;
            object value;
            return map != null && map.TryGetValue(key, out value) ? value : null;
        }

        static string Scalar(object item, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = Value(item, key);
                if (value is string && !string.IsNullOrWhiteSpace((string)value)) return (string)value;
                if (value is int || value is long || value is decimal) return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            return "";
        }

        static DateTimeOffset? Time(string raw)
        {
            long value;
            if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value <= 0) return null;
            // Observed history uses milliseconds or microseconds; some exports use Unix seconds.
            long subMillisecondTicks = 0;
            if (value >= 100000000000000L)
            {
                subMillisecondTicks = (value % 1000) * 10;
                value /= 1000;
            }
            else if (value < 10000000000L) value *= 1000;
            if (value > 253402300799999L) return null;
            return DateTimeOffset.FromUnixTimeMilliseconds(value).AddTicks(subMillisecondTicks);
        }
    }
}
