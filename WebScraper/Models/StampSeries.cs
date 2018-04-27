using System;
using System.Collections.Generic;

namespace WebScraper
{
    public class StampSeries
    {
        public string Title { get; set; }

        public int Year { get; set; }

        public string Details { get; set; }

        public string Comment { get; set; }

        public string Issue { get; set; }

        public DateTime Date { get; set; }

        public List<Stamp> Stamps { get; set; }
    }
}
