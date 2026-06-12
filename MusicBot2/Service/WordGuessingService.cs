using Discord;
using Discord.WebSocket;
using MusicBot2.Helpers;
using MusicBot2.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class WordGuessingService
    {
        public WordsGuessingVM Answer;
        private readonly WordGuessingService _wordService;
        private readonly GetChampService _getChampService;
        private const string FirstWords = "pornhubersSoBig";
        public WordGuessingService()
        {
            Answer = null;
        }

        public async Task<string> Guess(IMessageChannel channel,string word, SocketGuildUser user)
        {
            try
            {
                // 如果沒有提供猜測的單字，且目前沒有答案，就開始新的遊戲
                if (Answer == null)
                {
                    var words = LoadWords();

                    Random r = new Random();
                    var answerVM = words[r.Next(words.Count)];

                    Answer = answerVM;
                    Console.WriteLine($"正確答案: {Answer.word}");

                    //先幫他猜一個單字
                    string FirstGuessingWords = FirstWords.Substring(0, answerVM.word.Length);

                    var result = CheckWord(Answer.word, FirstGuessingWords);

                    var display = Display(FirstGuessingWords, result);

                    return $"開始猜瞜，這次的文字是 {answerVM.word.Length} 個字，啊我先幫你猜一個了不客氣\n" + display;

                }
                else
                {
                    if(word.Length != Answer.word.Length)
                    {
                        return $"字數錯啦，你是唐寶愛音484? 要猜 {Answer.word.Length} 個字的單字，你猜這什麼鬼? {word}";
                    }
                    var result = CheckWord(Answer.word, word);

                    var display = Display(word, result);

                    if (word.ToLower() == Answer.word)
                    {
                        display += $"\n\n🎉 猜對了我的寶\n單字: **{Answer.word}**\n意思: {Answer.translate} \n 獎勵 {user.DisplayName} {await RewardsHelpers.GetRandomRewards(channel, user)}";
                        Answer = null;
                    }
                    return display;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return null;
            }
        }

        public async Task<bool> SetWord(IAttachment file)
        {
            try
            {
                string _filePath = Path.Combine(Directory.GetCurrentDirectory(), "words.txt");

                using var httpClient = new HttpClient();
                var bytes = await httpClient.GetByteArrayAsync(file.Url);
                var content = Encoding.UTF8.GetString(bytes);

                if (string.IsNullOrWhiteSpace(content))
                    return false;

                await File.WriteAllTextAsync(_filePath, content, Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string CheckWord(string answer, string guess)
        {
            answer = answer.ToUpper();
            guess = guess.ToUpper();

            char[] result = new char[guess.Length];
            bool[] used = new bool[answer.Length];

            // 1. 先找位置正確
            for (int i = 0; i < guess.Length; i++)
            {
                if (guess[i] == answer[i])
                {
                    result[i] = 'G'; // Green
                    used[i] = true;
                }
            }

            // 2. 再找字正確但位置錯
            for (int i = 0; i < guess.Length; i++)
            {
                if (result[i] == 'G') continue;

                for (int j = 0; j < answer.Length; j++)
                {
                    if (!used[j] && guess[i] == answer[j])
                    {
                        result[i] = 'Y'; // Yellow
                        used[j] = true;
                        break;
                    }
                }

                if (result[i] == '\0')
                    result[i] = 'B'; // Black
            }

            return new string(result);
        }

        string Display(string guess, string result)
        {
            string output = "";

            for (int i = 0; i < guess.Length; i++)
            {
                if (result[i] == 'G')
                    output += $"🟩{guess[i]} ";
                else if (result[i] == 'Y')
                    output += $"🟨{guess[i]} ";
                else
                    output += $"⬛{guess[i]} ";
            }

            return output;
        }

        public static List<WordsGuessingVM> LoadWords()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "words.txt");
            var lines = File.ReadAllLines(path);

            List<WordsGuessingVM> list = new();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(' ', 2); // 只切第一個空格

                if (parts.Length < 2)
                    continue;

                list.Add(new WordsGuessingVM
                {
                    word = parts[0].Trim().ToLower(),
                    translate = parts[1].Trim()
                });
            }

            return list;
        }
    }
}
