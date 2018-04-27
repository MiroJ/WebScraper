using System;

namespace WebScraper
{
    class Program
    {
        static void Main(string[] args)
        {
            var scraper = new ScraperMtitc();
            scraper.Start();

            Console.ReadKey();
        }
    }
}
