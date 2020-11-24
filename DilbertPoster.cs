using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DilbertPoster
{
    public static class DilbertPoster
    {
        [FunctionName(nameof(GetAndPostStripAsync))]
        public static async System.Threading.Tasks.Task GetAndPostStripAsync([TimerTrigger("0 0 9 * * *")]TimerInfo myTimer, ILogger log)
        {
            // <img class="img-responsive img-comic" width="xxx" height="xxx" alt="xxxxx - Dilbert by Scott Adams" src="xxxxx" />
            Regex dilbertStrip = new Regex(@"<img class=""img-responsive img-comic"" width=""([0-9]+)"" height=""([0-9]+)"" alt=""(.+)"" src=""(.+)"" />");

            TelegramBotClient HorseBot = new TelegramBotClient(Environment.GetEnvironmentVariable("HorseBotKey"));
            string channel = Environment.GetEnvironmentVariable("Channel");
            string jackChat = Environment.GetEnvironmentVariable("JackChat");

#if DEBUG
            channel = jackChat;
#endif

            try
            {
                string html = await ReadPage("http://dilbert.com");
                var matches = dilbertStrip.Matches(html);

                //first match, 1 all, 2 width, 3 height, 4 title , 5 strip!
                string title = matches[0].Groups[3].ToString();
                string strip = matches[0].Groups[4].ToString();

                //sometimes the strip's url starts without "https:"
                if (!strip.StartsWith("https:"))
                    strip = "https:" + strip;

                Message message = await HorseBot.SendPhotoAsync(channel,
                            photo: strip,
                            caption: title,
                            parseMode: ParseMode.Html);
            }
            catch (Exception ex)
            {
                var error = HorseBot.SendTextMessageAsync(jackChat, "DilbertPoster Error: " + ex.ToString());
                log.LogError(ex.ToString());
            }
        }

        private static async Task<string> ReadPage(string url)
        {
            var request = WebRequest.CreateHttp(url);
            var tResp = request.GetResponseAsync();
            var tTimeout = Task.Delay(30 * 1000);
            if (await Task.WhenAny(tResp, tTimeout) == tTimeout)
            {
                throw new TimeoutException($"Timeout while getting data from {url}");
            }
            else
            {
                var response = await tResp;
                var stream = response.GetResponseStream();
                string html = "";
                using (StreamReader sr = new StreamReader(stream))
                {
                    html = await sr.ReadToEndAsync();
                }
                return html;
            }
        }
    }
}
