using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;

namespace DilbertPoster
{
    public static class DilbertPoster
    {

        private const bool runOnStartup = false;

        [FunctionName(nameof(GetAndPostStripAsync))]
        public static async Task GetAndPostStripAsync([TimerTrigger("0 15 9 * * *", RunOnStartup = runOnStartup)]TimerInfo myTimer, ILogger log)
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
                            // trick the Telegram API into thinking it's a gif 
                            photo: new InputOnlineFile(strip + ".gif"),
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
            var client = new HttpClient();
            var tResp = client.GetAsync(url);
            var tTimeout = Task.Delay(30 * 1000);
            if (await Task.WhenAny(tResp, tTimeout) == tTimeout)
            {
                throw new TimeoutException($"Timeout while getting data from {url}");
            }
            else
            {
                var response = await tResp;
                var stream = await response.Content.ReadAsStreamAsync();
                string html = "";
                using (StreamReader sr = new(stream))
                {
                    html = await sr.ReadToEndAsync();
                }
                return html;
            }
        }
    }
}
