using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;

namespace DilbertPoster
{
    public static class DurableDilbertPoster
    {
        private const bool runOnStartup = false;

        [FunctionName(nameof(DilbertPoster_Timer))]
        public static async Task DilbertPoster_Timer(
            [TimerTrigger("0 0 9 * * *", RunOnStartup = runOnStartup)] TimerInfo dilbertTimer,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            string instanceId = await starter.StartNewAsync(nameof(DilbertPoster_Orchestrator), null, currentDate);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }

        [FunctionName(nameof(DilbertPoster_Orchestrator))]
        public static async Task DilbertPoster_Orchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            string currentDate = context.GetInput<string>();
            if (!context.IsReplaying)
                log.LogInformation($"Starting strip search for {currentDate}...");

            int pollingInterval = 5 * 60; //5 minutes
            DateTime expiryTime = context.CurrentUtcDateTime.AddHours(1);

            while (context.CurrentUtcDateTime < expiryTime)
            {
                var data = await context.CallActivityAsync<StripData>(nameof(ReadPageContent), currentDate);
                if (!data.IsError)
                {
                    log.LogInformation("Strip found!");
                    await context.CallActivityAsync(nameof(SendToTelegram), data);
                    log.LogInformation("Sent!");
                    return;
                }

                log.LogInformation("Still waiting...");
                var nextCheck = context.CurrentUtcDateTime.AddSeconds(pollingInterval);
                await context.CreateTimer(nextCheck, CancellationToken.None);
            }

            log.LogError($"Timeout waiting for the strip {currentDate} to be released");
        }

        [FunctionName(nameof(ReadPageContent))]
        public static async Task<StripData> ReadPageContent([ActivityTrigger] string currentDate, ILogger log)
        {
            string url = "http://dilbert.com";

            var client = new HttpClient();
            var tResp = client.GetAsync(url);
            var tTimeout = Task.Delay(30 * 1000);
            if (await Task.WhenAny(tResp, tTimeout) == tTimeout)
            {
                var ex = new TimeoutException($"Timeout while getting html from {url}");
                return new StripData("Err", ex.ToString(), true);
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
                
                //find today's div
                //<div class="comic-item-container js-comic js-comic-container-{yyyy-MM-dd}"

                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                string xpathNode = $@"//div[@class='comic-item-container js-comic js-comic-container-{currentDate}'][1]";

                var div = doc.DocumentNode.SelectSingleNode(xpathNode);

                if (div is null)
                    return new StripData(null, null, true);

                string title = div.Attributes["data-title"].Value;
                string address = div.Attributes["data-image"].Value;
                
                if (!address.StartsWith("https:"))
                    address = "https:" + address;
                
                return new StripData(title, address, false);
            }
        }

        [FunctionName(nameof(SendToTelegram))]
        public static async Task SendToTelegram([ActivityTrigger] StripData strip, ILogger log)
        {

            TelegramBotClient HorseBot = new(Environment.GetEnvironmentVariable("HorseBotKey"));
            string channel = Environment.GetEnvironmentVariable("Channel");
            string jackChat = Environment.GetEnvironmentVariable("JackChat");

#if DEBUG
            channel = jackChat;
#endif

            Message message = await HorseBot.SendPhotoAsync(channel,
                        // trick the Telegram API into thinking it's a gif 
                        photo: new InputOnlineFile(strip.Address + ".gif"),
                        caption: strip.Title,
                        parseMode: ParseMode.Html);
        }

        public record StripData(string Title, string Address, bool IsError);

    }
}