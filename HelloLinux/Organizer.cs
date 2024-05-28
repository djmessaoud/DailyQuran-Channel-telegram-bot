using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
namespace HelloLinux
{
    internal class Organizer
    {
        private static readonly HttpClient client = new HttpClient();

        //Add function to check if the current day is beginning of new Hijri month, if yes, send a message to the channel and download the new month's prayer times

        Organizer()
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.5");
            client.DefaultRequestHeaders.Referrer = new Uri("http://www.google.com");

        }
        //Get the number of days in current Hijri month
        public static async Task<int> GetDaysInHijriMonth(int month, int year)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.5");
            client.DefaultRequestHeaders.Referrer = new Uri("http://www.google.com");
            string url = $"https://www.islamicfinder.org/prayer-times/printmonthlyprayer/?timeInterval=month&month={month}&year={year}&calendarType=Hijri";
            HttpResponseMessage response = await client.GetAsync(url);
            var html = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var dayNodes = doc.DocumentNode.SelectNodes("//tr[@class='row-body']");
            return dayNodes.Count;
        }
    }
}
