using System;
using System.Collections.Generic;

namespace WebScraper
{
    public class StampSeries
    {
        public string Title { get; set; }

        public string Details { get; set; }

        public string Comment { get; set; }

        public DateTime Date { get; set; }

        public string Quantities { get; internal set; }

        public List<Stamp> Stamps { get; set; }

        internal bool HasData
        {
            get
            {
                return string.IsNullOrEmpty(Title) == false || 
                    string.IsNullOrEmpty(Details) == false || 
                    string.IsNullOrEmpty(Comment) == false || 
                    string.IsNullOrEmpty(Quantities) == false || 
                    Stamps.Count > 0;
            }
        }

    }
}
