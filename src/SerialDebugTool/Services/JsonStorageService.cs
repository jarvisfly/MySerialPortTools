using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SerialDebugTool.Services;

/// <summary>
/// 本地化 JSON 存取：文件固定保存在“软件所在目录”（可执行文件同级目录）。
/// </summary>
public static class JsonStorageService
{
    public const string SettingsFile = "settings.json";
    public const string SendCommandFile = "send_commands.json";
    public const string ReceiveCommandFile = "receive_commands.json";

    private static readonly object SyncRoot = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>软件所在目录（exe / dll 同级）。</summary>
    public static string AppDirectory => AppContext.BaseDirectory;

    /// <summary>配置文件的完整路径。</summary>
    public static string GetFullPath(string fileName) => Path.Combine(AppDirectory, fileName);

    /// <summary>读取配置；文件不存在或损坏时返回 null（调用方给默认值）。</summary>
    public static T? Load<T>(string fileName) where T : class
    {
        var path = GetFullPath(fileName);
        lock (SyncRoot)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<T>(json, Options);
            }
            catch (Exception ex)
            {
                // 配置损坏不应导致程序起不来：备份坏文件后返回 null
                TryBackupCorrupted(path, ex);
                return null;
            }
        }
    }

    /// <summary>保存配置（原子写：先写临时文件再替换）。</summary>
    public static bool Save<T>(string fileName, T value)
    {
        var path = GetFullPath(fileName);
        var tmp = path + ".tmp";
        lock (SyncRoot)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
                if (File.Exists(path))
                {
                    File.Copy(tmp, path + ".bak", overwrite: true);
                    File.Delete(path);
                }

                File.Move(tmp, path);
                return true;
            }
            catch (Exception)
            {
                try
                {
                    if (File.Exists(tmp))
                    {
                        File.Delete(tmp);
                    }
                }
                catch
                {
                    // 忽略清理失败
                }

                return false;
            }
        }
    }

    private static void TryBackupCorrupted(string path, Exception ex)
    {
        try
        {
            var bak = path + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(path, bak, overwrite: true);
            File.AppendAllText(bak, Environment.NewLine + "// 解析失败: " + ex.Message);
        }
        catch
        {
            // 忽略
        }
    }
}
