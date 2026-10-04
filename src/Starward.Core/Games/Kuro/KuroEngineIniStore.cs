using System.Text;
using System.Text.RegularExpressions;

namespace Starward.Core.Games.Kuro;

/// <summary>
/// 读写鸣潮 <c>Saved\Config\WindowsNoEditor</c> 下的 Engine.ini 与 Input.ini。
/// <para/>
/// 只动 <see cref="KuroEngineTweakCatalog"/> 列出的键：文件里其他节（游戏生成的
/// <c>[Core.System]</c> Paths、无障碍设置等）与玩家自己加的键一律原样保留，
/// 行尾、编码与行序也不变。写之前先备份为 <c>*.starward.bak</c>。
/// </summary>
public static partial class KuroEngineIniStore
{

    public const string EngineIniFileName = "Engine.ini";

    public const string InputIniFileName = "Input.ini";

    public const string BackupSuffix = ".starward.bak";


    /// <summary>
    /// 读出目录里所有调校项的现值，没写的键不在结果里
    /// </summary>
    /// <param name="configDirectory"><see cref="KuroGameMapping.GetSavedConfigDirectory"/></param>
    public static Dictionary<KuroEngineTweak, string> Read(string configDirectory)
    {
        var result = new Dictionary<KuroEngineTweak, string>();
        foreach (var group in KuroEngineTweakCatalog.Tweaks.GroupBy(x => x.File))
        {
            string path = Path.Join(configDirectory, GetFileName(group.Key));
            if (!File.Exists(path))
            {
                continue;
            }
            IniDocument doc = IniDocument.Load(path);
            foreach (KuroEngineTweak tweak in group)
            {
                if (doc.Get(GetSections(tweak), tweak.Key) is string value)
                {
                    result[tweak] = value;
                }
            }
        }
        return result;
    }


    /// <summary>
    /// 写入调校项。值为 null 表示删除这个键（让游戏用默认值）；不在字典里的键不动。
    /// </summary>
    /// <returns>实际改写了的文件</returns>
    public static List<string> Write(string configDirectory, IReadOnlyDictionary<KuroEngineTweak, string?> values)
    {
        var written = new List<string>();
        foreach (var group in values.GroupBy(x => x.Key.File))
        {
            string path = Path.Join(configDirectory, GetFileName(group.Key));
            bool exists = File.Exists(path);
            if (!exists && group.All(x => x.Value is null))
            {
                continue;
            }
            IniDocument doc = exists ? IniDocument.Load(path) : IniDocument.Empty();
            foreach (var (tweak, value) in group)
            {
                doc.Set(GetSections(tweak), tweak.Key, value?.Trim());
            }
            if (!doc.IsChanged)
            {
                continue;
            }
            Directory.CreateDirectory(configDirectory);
            if (exists)
            {
                File.Copy(path, path + BackupSuffix, true);
            }
            doc.Save(path);
            written.Add(path);
        }
        return written;
    }


    private static string GetFileName(KuroEngineIniFile file) => file switch
    {
        KuroEngineIniFile.Input => InputIniFileName,
        _ => EngineIniFileName,
    };


    /// <summary>
    /// 第一个是新键要写进的节；<c>[SystemSettings]</c> 的控制台变量也认
    /// <c>[ConsoleVariables]</c> 里的同名键，原本写在那里的就留在那里改。
    /// </summary>
    private static string[] GetSections(KuroEngineTweak tweak)
    {
        return tweak.Section == KuroEngineTweakCatalog.SystemSettings
            ? [KuroEngineTweakCatalog.SystemSettings, KuroEngineTweakCatalog.ConsoleVariables]
            : [tweak.Section];
    }



    /// <summary>
    /// 按行保存的 ini，改动只发生在被 <see cref="Set"/> 的那几行
    /// </summary>
    internal sealed partial class IniDocument
    {

        private readonly List<string> _lines;

        private readonly Encoding _encoding;

        private readonly string _newLine;

        private readonly bool _endsWithNewLine;

        public bool IsChanged { get; private set; }


        private IniDocument(List<string> lines, Encoding encoding, string newLine, bool endsWithNewLine)
        {
            _lines = lines;
            _encoding = encoding;
            _newLine = newLine;
            _endsWithNewLine = endsWithNewLine;
        }


        public static IniDocument Empty() => new([], new UTF8Encoding(false), "\r\n", true);


        public static IniDocument Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Encoding encoding = DetectEncoding(bytes);
            string text = encoding.GetString(bytes);
            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text[1..];
            }
            string newLine = text.Contains("\r\n") || !text.Contains('\n') ? "\r\n" : "\n";
            bool endsWithNewLine = text.EndsWith('\n');
            List<string> lines = text.Split('\n').Select(x => x.TrimEnd('\r')).ToList();
            if (endsWithNewLine)
            {
                // Split 在结尾换行之后多出一个空字符串
                lines.RemoveAt(lines.Count - 1);
            }
            return new IniDocument(lines, encoding, newLine, endsWithNewLine);
        }


        /// <summary>
        /// 只认带 BOM 的 UTF-8 / UTF-16；虚幻存纯 ASCII 的 ini 时不带 BOM，按无 BOM 的 UTF-8 写回
        /// </summary>
        private static Encoding DetectEncoding(byte[] bytes)
        {
            if (bytes is [0xEF, 0xBB, 0xBF, ..])
            {
                return new UTF8Encoding(true);
            }
            if (bytes is [0xFF, 0xFE, ..])
            {
                return new UnicodeEncoding(false, true);
            }
            if (bytes is [0xFE, 0xFF, ..])
            {
                return new UnicodeEncoding(true, true);
            }
            return new UTF8Encoding(false);
        }


        public void Save(string path)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _lines.Count; i++)
            {
                sb.Append(_lines[i]);
                if (i < _lines.Count - 1 || _endsWithNewLine)
                {
                    sb.Append(_newLine);
                }
            }
            // GetPreamble 只在构造时要求了 BOM 才有内容
            byte[] preamble = _encoding.GetPreamble();
            byte[] body = _encoding.GetBytes(sb.ToString());
            using var fs = File.Create(path);
            fs.Write(preamble);
            fs.Write(body);
        }


        public string? Get(string[] sections, string key)
        {
            foreach (string section in sections)
            {
                foreach (int index in FindKeyLines(section, key))
                {
                    return KeyLineRegex().Match(_lines[index]).Groups[2].Value.Trim();
                }
            }
            return null;
        }


        /// <summary>
        /// 设置或删除一个键。已经存在的就地改第一处、删掉其余重复；
        /// 不存在的写到首选节的最后一个键之后，没有这一节就在文件末尾新建。
        /// </summary>
        public void Set(string[] sections, string key, string? value)
        {
            var occurrences = sections.SelectMany(s => FindKeyLines(s, key)).OrderBy(x => x).ToList();
            if (value is null)
            {
                for (int i = occurrences.Count - 1; i >= 0; i--)
                {
                    _lines.RemoveAt(occurrences[i]);
                    IsChanged = true;
                }
                return;
            }
            string line = $"{key}={value}";
            if (occurrences.Count > 0)
            {
                for (int i = occurrences.Count - 1; i >= 1; i--)
                {
                    _lines.RemoveAt(occurrences[i]);
                    IsChanged = true;
                }
                if (_lines[occurrences[0]] != line)
                {
                    _lines[occurrences[0]] = line;
                    IsChanged = true;
                }
                return;
            }
            (int start, int end)? range = FindSection(sections[0]);
            if (range is null)
            {
                if (_lines.Count > 0 && !string.IsNullOrWhiteSpace(_lines[^1]))
                {
                    _lines.Add("");
                }
                _lines.Add($"[{sections[0]}]");
                _lines.Add(line);
            }
            else
            {
                // 插在节内最后一个非空行之后，节与节之间的空行保持在原位
                int insertAt = range.Value.start + 1;
                for (int i = range.Value.end - 1; i > range.Value.start; i--)
                {
                    if (!string.IsNullOrWhiteSpace(_lines[i]))
                    {
                        insertAt = i + 1;
                        break;
                    }
                }
                _lines.Insert(insertAt, line);
            }
            IsChanged = true;
        }


        /// <summary>
        /// 节标题所在行，与下一节标题（或文件结尾）所在行
        /// </summary>
        private (int start, int end)? FindSection(string section)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                Match match = SectionRegex().Match(_lines[i]);
                if (match.Success && string.Equals(match.Groups[1].Value.Trim(), section, StringComparison.OrdinalIgnoreCase))
                {
                    int end = i + 1;
                    while (end < _lines.Count && !SectionRegex().IsMatch(_lines[end]))
                    {
                        end++;
                    }
                    return (i, end);
                }
            }
            return null;
        }


        private IEnumerable<int> FindKeyLines(string section, string key)
        {
            bool inSection = false;
            for (int i = 0; i < _lines.Count; i++)
            {
                Match sectionMatch = SectionRegex().Match(_lines[i]);
                if (sectionMatch.Success)
                {
                    inSection = string.Equals(sectionMatch.Groups[1].Value.Trim(), section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (inSection)
                {
                    Match keyMatch = KeyLineRegex().Match(_lines[i]);
                    if (keyMatch.Success && string.Equals(keyMatch.Groups[1].Value.Trim(), key, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return i;
                    }
                }
            }
        }


        [GeneratedRegex(@"^\s*\[(.+)\]\s*$")]
        private static partial Regex SectionRegex();


        /// <summary>
        /// <c>Key=Value</c>，跳过 <c>;</c> 开头的注释；带 <c>+-.!</c> 前缀的数组操作不当作同一个键
        /// </summary>
        [GeneratedRegex(@"^\s*([^;\s+\-.!=][^=]*?)\s*=(.*)$")]
        private static partial Regex KeyLineRegex();

    }

}
