using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RestStop
{
    /// <summary>
    /// 다국어. Resources/Localization/strings.csv 한 장으로 관리한다.
    /// 첫 줄 = key, en, ko, ja ... 비어 있는 칸은 영어로 대체한다.
    /// </summary>
    public static class Loc
    {
        public static readonly string[] Codes =
        {
            "en", "ko", "ja", "zh-Hans", "zh-Hant", "fr", "de", "es", "es-419",
            "pt-BR", "ru", "it", "pl", "tr", "th", "vi"
        };
        public static readonly string[] Names =
        {
            "English", "한국어", "日本語", "简体中文", "繁體中文", "Français", "Deutsch", "Español",
            "Español (Latinoamérica)", "Português (Brasil)", "Русский", "Italiano", "Polski", "Türkçe", "ไทย", "Tiếng Việt"
        };

        static readonly Dictionary<string, string[]> _table = new();
        static int _lang;
        static bool _ready;

        public static string CurrentName => Names[_lang];
        public static string CurrentCode => Codes[_lang];

        public static void Init()
        {
            if (_ready) return;
            _ready = true;
            var asset = Resources.Load<TextAsset>("Localization/strings");
            if (asset != null) Parse(asset.text);
            int saved = PlayerPrefs.GetInt("lang", -1);
            _lang = saved >= 0 && saved < Codes.Length ? saved : FromSystem(Application.systemLanguage);
        }

        public static void Next()
        {
            _lang = (_lang + 1) % Codes.Length;
            PlayerPrefs.SetInt("lang", _lang);
        }

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (!_ready) Init();
            if (!_table.TryGetValue(key, out var row)) return key;
            if (_lang < row.Length && !string.IsNullOrEmpty(row[_lang])) return row[_lang];
            return string.IsNullOrEmpty(row[0]) ? key : row[0];
        }

        public static string Get(string key, params object[] args)
        {
            var s = Get(key);
            try { return string.Format(s, args); } catch { return s; }
        }

        static int FromSystem(SystemLanguage l) => l switch
        {
            SystemLanguage.Korean => 1,
            SystemLanguage.Japanese => 2,
            SystemLanguage.ChineseSimplified or SystemLanguage.Chinese => 3,
            SystemLanguage.ChineseTraditional => 4,
            SystemLanguage.French => 5,
            SystemLanguage.German => 6,
            SystemLanguage.Spanish => 7,
            SystemLanguage.Portuguese => 9,
            SystemLanguage.Russian => 10,
            SystemLanguage.Italian => 11,
            SystemLanguage.Polish => 12,
            SystemLanguage.Turkish => 13,
            SystemLanguage.Thai => 14,
            SystemLanguage.Vietnamese => 15,
            _ => 0
        };

        // 따옴표와 쉼표, 줄바꿈을 지원하는 간단한 CSV 파서
        static void Parse(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\n')
                {
                    row.Add(cell.ToString().TrimEnd('\r')); cell.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }

            if (rows.Count == 0) return;
            // 헤더 순서를 Codes 순서로 맞춘다
            var header = rows[0];
            var map = new int[Codes.Length];
            for (int k = 0; k < Codes.Length; k++) map[k] = header.IndexOf(Codes[k]);

            for (int r = 1; r < rows.Count; r++)
            {
                var src = rows[r];
                if (src.Count == 0 || string.IsNullOrWhiteSpace(src[0])) continue;
                var vals = new string[Codes.Length];
                for (int k = 0; k < Codes.Length; k++)
                    vals[k] = map[k] >= 0 && map[k] < src.Count ? src[map[k]].Replace("\\n", "\n") : "";
                _table[src[0].Trim()] = vals;
            }
        }
    }
}
