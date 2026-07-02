using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicBot2.Helpers
{
    public static class JapaneseTextHelper
    {
        /// <summary>
        /// 檢測字符是否為漢字（日文漢字範圍）
        /// </summary>
        public static bool IsKanji(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) ||  // CJK統一表意文字
                   (c >= 0x3400 && c <= 0x4DBF) ||  // CJK統一表意文字擴展A
                   (c >= 0xF900 && c <= 0xFAFF);    // CJK相容表意文字
        }

        /// <summary>
        /// 檢測字符是否為平假名
        /// </summary>
        public static bool IsHiragana(char c)
        {
            return c >= 0x3040 && c <= 0x309F;
        }

        /// <summary>
        /// 檢測字符是否為片假名
        /// </summary>
        public static bool IsKatakana(char c)
        {
            return c >= 0x30A0 && c <= 0x30FF;
        }

        /// <summary>
        /// 將片假名轉換為平假名
        /// </summary>
        public static string KatakanaToHiragana(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if (IsKatakana(c))
                {
                    // 片假名轉平假名：減去0x60
                    sb.Append((char)(c - 0x60));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 檢測字符串是否包含日文字符
        /// </summary>
        public static bool ContainsJapanese(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return text.Any(c => IsKanji(c) || IsHiragana(c) || IsKatakana(c));
        }

        /// <summary>
        /// 將歌詞行標記需要讀音的部分（漢字和片假名）
        /// 由於沒有完整的形態分析，我們只標記字符類型
        /// </summary>
        public static string MarkJapaneseText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new StringBuilder();
            var kanjiBuffer = new StringBuilder();
            var katakanaBuffer = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (IsKanji(c))
                {
                    // 清空片假名緩衝
                    if (katakanaBuffer.Length > 0)
                    {
                        sb.Append($"[{katakanaBuffer}]");
                        katakanaBuffer.Clear();
                    }
                    kanjiBuffer.Append(c);
                }
                else if (IsKatakana(c))
                {
                    // 清空漢字緩衝
                    if (kanjiBuffer.Length > 0)
                    {
                        sb.Append($"[{kanjiBuffer}]");
                        kanjiBuffer.Clear();
                    }
                    katakanaBuffer.Append(c);
                }
                else
                {
                    // 清空所有緩衝
                    if (kanjiBuffer.Length > 0)
                    {
                        sb.Append($"[{kanjiBuffer}]");
                        kanjiBuffer.Clear();
                    }
                    if (katakanaBuffer.Length > 0)
                    {
                        sb.Append($"[{katakanaBuffer}]");
                        katakanaBuffer.Clear();
                    }
                    sb.Append(c);
                }
            }

            // 清空剩餘緩衝
            if (kanjiBuffer.Length > 0)
            {
                sb.Append($"[{kanjiBuffer}]");
            }
            if (katakanaBuffer.Length > 0)
            {
                sb.Append($"[{katakanaBuffer}]");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 簡化版：將歌詞中的片假名轉換為平假名並標註
        /// 例如：カタカナ → カタカナ(かたかな)
        /// </summary>
        public static string AddKatakanaFurigana(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new StringBuilder();
            var katakanaBuffer = new StringBuilder();

            var kanjiBuffer = new StringBuilder();
            bool isKanjiMode = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (IsKanji(c))
                {
                    // 先處理片假名緩衝
                    if (katakanaBuffer.Length > 0)
                    {
                        string katakana = katakanaBuffer.ToString();
                        string hiragana = KatakanaToHiragana(katakana);
                        sb.Append($"{katakana}ᐧ{hiragana}");
                        katakanaBuffer.Clear();
                    }
                    kanjiBuffer.Append(c);
                    isKanjiMode = true;
                }
                else if (IsKatakana(c))
                {
                    // 先處理漢字緩衝
                    if (kanjiBuffer.Length > 0)
                    {
                        string kanji = kanjiBuffer.ToString();
                        sb.Append($"{kanji}ᐧ˙");
                        kanjiBuffer.Clear();
                        isKanjiMode = false;
                    }
                    katakanaBuffer.Append(c);
                }
                else
                {
                    // 清空所有緩衝
                    if (kanjiBuffer.Length > 0)
                    {
                        string kanji = kanjiBuffer.ToString();
                        sb.Append($"{kanji}ᐧ˙");
                        kanjiBuffer.Clear();
                        isKanjiMode = false;
                    }
                    if (katakanaBuffer.Length > 0)
                    {
                        string katakana = katakanaBuffer.ToString();
                        string hiragana = KatakanaToHiragana(katakana);
                        sb.Append($"{katakana}ᐧ{hiragana}");
                        katakanaBuffer.Clear();
                    }
                    sb.Append(c);
                }
            }

            // 處理結尾的緩衝
            if (kanjiBuffer.Length > 0)
            {
                string kanji = kanjiBuffer.ToString();
                sb.Append($"{kanji}ᐧ˙");
            }
            if (katakanaBuffer.Length > 0)
            {
                string katakana = katakanaBuffer.ToString();
                string hiragana = KatakanaToHiragana(katakana);
                sb.Append($"{katakana}ᐧ{hiragana}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 檢測語言是否為日文
        /// </summary>
        public static bool IsLikelyJapanese(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            int japaneseCharCount = 0;
            int totalCharCount = 0;

            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c))
                {
                    totalCharCount++;
                    if (IsKanji(c) || IsHiragana(c) || IsKatakana(c))
                    {
                        japaneseCharCount++;
                    }
                }
            }

            // 如果超過30%是日文字符，判定為日文
            return totalCharCount > 0 && (japaneseCharCount / (double)totalCharCount) > 0.3;
        }
    }
}
