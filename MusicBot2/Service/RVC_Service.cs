using Discord;
using Discord.Audio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot2.Service
{
    public class RVC_Service
    {
        //打到我本地的位置:http://localhost:8000/api/v1/voice-convert post

        public RVC_Service() { }

        public async Task<Stream> GetConvertedAudioAsync(Stream audioStream, string fileName, string speaker, double pitch, double indexRate, double protect)
        {
            using var client = new HttpClient();
            using var form = new MultipartFormDataContent();
            var fileContent = new StreamContent(audioStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(fileContent, "audio", fileName);
            form.Add(new StringContent(speaker), "speaker");
            form.Add(new StringContent(pitch.ToString()), "pitch_shift");
            form.Add(new StringContent(indexRate.ToString()), "index_rate");
            form.Add(new StringContent(protect.ToString()), "protect");

            var response = await client.PostAsync("http://localhost:8000/api/v1/voice-convert", form);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync();
        }

        public async Task SendConvertedAudioToChannelAsync(ITextChannel channel, Stream audioStream, string fileName, string speaker, double pitch, double indexRate, double protect)
        {
            var convertedStream = await GetConvertedAudioAsync(audioStream, fileName, speaker, pitch, indexRate, protect);
            convertedStream.Position = 0;
            await channel.SendFileAsync(convertedStream, $"{speaker}_output.wav", "轉換後的音頻");
        }

        public async Task SendTextToSpeach(ITextChannel channel,string text,string speaker,string tts_model,double pitch_shift)
        {
            using var client = new HttpClient();
            using var form = new MultipartFormDataContent();

            form.Add(new StringContent(text), "text");
            form.Add(new StringContent(speaker), "speaker");
            form.Add(new StringContent(pitch_shift.ToString()), "pitch_shift");

            //這邊先寫死
            form.Add(new StringContent(tts_model), "voice");

            var response = await client.PostAsync("http://localhost:8000/api/v1/tts", form);
            response.EnsureSuccessStatusCode();

            var convertedStream = await response.Content.ReadAsStreamAsync();

            await channel.SendFileAsync(convertedStream, $"{speaker}_output.wav", "");
        }
    }
}
