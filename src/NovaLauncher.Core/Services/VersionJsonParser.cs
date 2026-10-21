using NovaLauncher.Core.Models;
using Newtonsoft.Json.Linq;

namespace NovaLauncher.Core.Services;

/// <summary>解析官方 version json 为模型。</summary>
public static class VersionJsonParser
{
    public static MinecraftVersion Parse(string json, string fallbackId = "")
    {
        var root = JObject.Parse(json);
        var version = new MinecraftVersion
        {
            Id = root["id"]?.ToString() ?? fallbackId,
            Type = root["type"]?.ToString() ?? "release",
            MainClass = root["mainClass"]?.ToString() ?? "net.minecraft.client.main.Main",
            Assets = root["assets"]?.ToString() ?? "",
        };

        // asset index
        if (root["assetIndex"] is JObject ai)
        {
            version.AssetIndex = new AssetIndexInfo
            {
                Id = ai["id"]?.ToString() ?? version.Assets,
                Url = ai["url"]?.ToString() ?? "",
                Size = ai["size"]?.ToObject<long>() ?? 0,
                Total = ai["totalSize"]?.ToObject<long>() ?? 0,
            };
        }

        // client jar
        if (root["downloads"]?["client"] is JObject client)
        {
            version.Client = new DownloadInfo
            {
                Url = client["url"]?.ToString() ?? "",
                Sha1 = client["sha1"]?.ToString() ?? "",
                Size = client["size"]?.ToObject<long>() ?? 0,
            };
        }

        // libraries
        if (root["libraries"] is JArray libs)
        {
            foreach (var lib in libs.OfType<JObject>())
                version.Libraries.Add(ParseLibrary(lib));
        }

        // java version（字段名为 majorVersion）
        var javaMajor = root["javaVersion"]?["majorVersion"]?.ToObject<int>();
        if (javaMajor.HasValue) version.JavaMajorVersion = javaMajor.Value;

        // arguments (new style)
        var args = root["arguments"];
        if (args is JObject argsObj)
        {
            if (argsObj["game"] is JArray g) version.GameArguments = ParseArguments(g);
            if (argsObj["jvm"] is JArray j) version.JvmArguments = ParseArguments(j);
        }
        else if (root["minecraftArguments"] is JValue legacy)
        {
            // 旧版：把整串按空白拆为参数（保留 ${} 占位符，启动时统一替换）
            var text = legacy.Value<string>() ?? "";
            version.GameArguments = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => new Argument { Values = new List<string> { v } })
                .ToList();
        }

        return version;
    }

    private static LibraryItem ParseLibrary(JObject lib)
    {
        var item = new LibraryItem
        {
            Name = lib["name"]?.ToString() ?? "",
        };
        item.NameParts = item.Name.Split(':');

        if (lib["downloads"]?["artifact"] is JObject artifact)
        {
            item.Artifact = new DownloadInfo
            {
                Path = artifact["path"]?.ToString(),
                Url = artifact["url"]?.ToString() ?? "",
                Sha1 = artifact["sha1"]?.ToString() ?? "",
                Size = artifact["size"]?.ToObject<long>() ?? 0,
            };
        }

        if (lib["downloads"]?["classifiers"] is JObject cls)
        {
            foreach (var prop in cls.Properties())
            {
                if (prop.Value is JObject co)
                {
                    item.Classifiers[prop.Name] = new DownloadInfo
                    {
                        Path = co["path"]?.ToString(),
                        Url = co["url"]?.ToString() ?? "",
                        Sha1 = co["sha1"]?.ToString() ?? "",
                        Size = co["size"]?.ToObject<long>() ?? 0,
                    };
                }
            }
        }

        if (lib["natives"] is JObject natives)
        {
            foreach (var prop in natives.Properties())
                item.Natives[prop.Name] = prop.Value?.ToString() ?? "";
        }

        if (lib["rules"] is JArray rules)
            item.Rules = ParseRules(rules);

        return item;
    }

    private static List<Argument> ParseArguments(JArray array)
    {
        var result = new List<Argument>();
        foreach (var token in array)
        {
            if (token is JValue value && value.Type == JTokenType.String)
            {
                result.Add(new Argument { Values = new List<string> { value.Value<string>() ?? "" } });
            }
            else if (token is JObject obj)
            {
                var arg = new Argument { Rules = ParseRules(obj["rules"] as JArray) };
                var val = obj["value"];
                if (val is JValue sv)
                {
                    arg.Values.Add(sv.Value<string>() ?? "");
                }
                else if (val is JArray av)
                {
                    foreach (var v in av.OfType<JValue>())
                        arg.Values.Add(v.Value<string>() ?? "");
                }
                result.Add(arg);
            }
        }
        return result;
    }

    private static List<Rule> ParseRules(JArray? rules)
    {
        var result = new List<Rule>();
        if (rules == null) return result;
        foreach (var r in rules.OfType<JObject>())
        {
            var rule = new Rule { Action = r["action"]?.ToString() ?? "allow" };
            if (r["os"] is JObject os)
            {
                rule.Os = new OsRule
                {
                    Name = os["name"]?.ToString() ?? "",
                    Arch = os["arch"]?.ToString() ?? "",
                };
            }
            if (r["features"] != null)
                rule.Features = new FeaturesRule();
            result.Add(rule);
        }
        return result;
    }
}
