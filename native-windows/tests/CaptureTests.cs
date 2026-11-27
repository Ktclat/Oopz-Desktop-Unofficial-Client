using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Oopz.Native;

public static class CaptureTests
{
    const string Host = "https://gateway-n.oopz.cn";
    const string Areas = "/userSubscribeArea/v1/list";
    const string Channels = "/client/v1/area/v1/detail/v1/channels";
    const string History = "/im/session/v2/messageBefore";
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    static object Entry(string url, object payload, int status = 200, bool base64 = false)
    {
        var text = Json.Serialize(payload);
        return new {
            request = new { url = url, method = "GET", headers = new[] { new { name = "ignored", value = "unrelated metadata" } } },
            response = new { status = status, content = new {
                text = base64 ? Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) : text,
                encoding = base64 ? "base64" : null
            }}
        };
    }

    static OopzSnapshot Import(params object[] entries)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Json.Serialize(new { log = new { entries = entries } }), new UTF8Encoding(false));
            return OopzCapture.Load(path);
        }
        finally { File.Delete(path); }
    }

    static void Assert(bool condition, string explanation)
    {
        if (!condition) throw new Exception(explanation);
    }

    public static int Main()
    {
        try
        {
            Run();
            Console.WriteLine("Oopz capture parser tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            var cause = error.GetBaseException();
            Console.WriteLine(cause.GetType().FullName + ": " + cause.Message);
            Console.WriteLine(error.StackTrace);
            return 1;
        }
    }

    public static void Run()
    {
        var grouped = new[] { new { name = "分组", channels = new[] { new { id = "chat", name = "聊天", type = "TEXT" } } } };
        var first = new { status = true, code = "200", data = new { messages = new object[] {
            new { messageId = "m1", person = "sender", text = "第一条", timestamp = "1700000000000321" },
            new { messageId = 123, content = "第二条", timestamp = 1700000001000L },
            new { messageId = "wrong", area = "elsewhere", text = "越过请求范围" }
        }}};
        var nextPage = new { messages = new object[] {
            new { id = 123, recall = true },
            new { messageId = 123, content = "已撤回的内容" }
        }};
        var scopes = "?area=a%2D1&channel=chat";
        var names = new[] { new { id = "a-1", name = "域一" }, new { id = "a-2", name = "域二" } };
        var empty = new object[0];
        var data = Import(
            Entry(Host + History + scopes, first, base64: true),
            Entry(Host + History + scopes + "&before=17", nextPage),
            Entry(Host + Areas, new { status = true, data = names }),
            Entry(Host + Channels + "?area=a-1", grouped),
            Entry(Host + Channels + "?area=a-2", grouped),
            Entry("https://gateway-n.oopz.cn.example.org" + Channels + "?area=foreign", grouped),
            Entry(Host + Channels + "?area=denied", new { status = false, code = "403", data = grouped }),
            Entry(Host + Channels + "?area=denied-http", grouped, status: 403),
            Entry(Host + "/login", new { data = grouped }));

        Assert(data.Areas.Count == 2 && data.Areas[0].Name == "域一", "Area identity or origin checks failed.");
        var channel = data.Areas[0].Channels.Single();
        Assert(channel.Group == "分组" && channel.Name == "聊天" && channel.Type == "TEXT", "A partial channel did not acquire its metadata.");
        Assert(channel.Messages.Count == 2 && !data.Areas[1].Channels.Single().Messages.Any(), "Channel isolation or pagination deduplication failed.");
        var message = channel.Messages.Single(m => m.Id == "m1");
        Assert(message.Author == "sender" && message.Text == "第一条" && message.Timestamp.Value.ToUnixTimeSeconds() == 1700000000 && message.Timestamp.Value.Ticks % 10000 == 3210, "Message parsing failed.");
        Assert(channel.Messages.Single(m => m.Id == "123").Text == "［消息已撤回］", "A recalled body reappeared.");
        Assert(Import(Entry(Host + Areas, new { status = true, data = empty })).Areas.Count == 0, "A successful empty list should be valid.");

        bool rejected = false;
        try { Import(Entry(Host + Areas, new { status = true, code = 403, data = names })); }
        catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "A failed service response was accepted as a successful import.");
    }
}
