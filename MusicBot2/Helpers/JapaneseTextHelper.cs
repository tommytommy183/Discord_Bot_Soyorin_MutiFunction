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
        /// 浪代才琌簙らゅ簙絛瞅
        /// </summary>
        public static bool IsKanji(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) ||  // CJK参種ゅ
                   (c >= 0x3400 && c <= 0x4DBF) ||  // CJK参種ゅ耎甶A
                   (c >= 0xF900 && c <= 0xFAFF);    // CJK甧種ゅ
        }

        /// <summary>
        /// 浪代才琌キ安
        /// </summary>
        public static bool IsHiragana(char c)
        {
            return c >= 0x3040 && c <= 0x309F;
        }

        /// <summary>
        /// 浪代才琌安
        /// </summary>
        public static bool IsKatakana(char c)
        {
            return c >= 0x30A0 && c <= 0x30FF;
        }

        /// <summary>
        /// 盢安锣传キ安
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
                    // 安锣キ安搭0x60
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
        /// 浪代才﹃琌らゅ才
        /// </summary>
        public static bool ContainsJapanese(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return text.Any(c => IsKanji(c) || IsHiragana(c) || IsKatakana(c));
        }

        /// <summary>
        /// 盢簈迭︽夹癘惠璶弄场だ簙㎝安
        /// パ⊿ΤЧ俱篈だ猂и夹癘才摸
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
                    // 睲安絯侥
                    if (katakanaBuffer.Length > 0)
                    {
                        sb.Append($"[{katakanaBuffer}]");
                        katakanaBuffer.Clear();
                    }
                    kanjiBuffer.Append(c);
                }
                else if (IsKatakana(c))
                {
                    // 睲簙絯侥
                    if (kanjiBuffer.Length > 0)
                    {
                        sb.Append($"[{kanjiBuffer}]");
                        kanjiBuffer.Clear();
                    }
                    katakanaBuffer.Append(c);
                }
                else
                {
                    // 睲┮Τ絯侥
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

            // 睲逞緇絯侥
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
        /// 虏て盢簈迭い安锣传キ安夹爹
        /// ㄒ???? △ ????(????)
        /// </summary>
        public static string AddKatakanaFurigana(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new StringBuilder();
            var katakanaBuffer = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (IsKatakana(c))
                {
                    katakanaBuffer.Append(c);
                }
                else
                {
                    if (katakanaBuffer.Length > 0)
                    {
                        string katakana = katakanaBuffer.ToString();
                        string hiragana = KatakanaToHiragana(katakana);
                        sb.Append($"{katakana}({hiragana})");
                        katakanaBuffer.Clear();
                    }
                    sb.Append(c);
                }
            }

            // 矪瞶挡Ю安
            if (katakanaBuffer.Length > 0)
            {
                string katakana = katakanaBuffer.ToString();
                string hiragana = KatakanaToHiragana(katakana);
                sb.Append($"{katakana}({hiragana})");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 浪代粂ē琌らゅ
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

            // 狦禬筁30%琌らゅ才﹚らゅ
            return totalCharCount > 0 && (japaneseCharCount / (double)totalCharCount) > 0.3;
        }
    }
}
