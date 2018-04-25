using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebScraper
{
    public class ScraperMtitc
    {
        #region - Declarations -

        string _url = @"https://www.mtitc.government.bg/archive/page.php?category=144&id=1434";

        #endregion

        #region - Public methods -

        public void Scrape()
        {
            // Get initial page
            var mainUri = new Uri(_url);
            var mainPageDom = GetDocumentNode(mainUri);

            // Get all main URIs
            var uriList = GetMainPageList(mainPageDom, mainUri);

            // Go to each page, ...
            foreach (var uri in uriList)
            {
                // ... load the first page, ...
                var doc = GetDocumentNode(uri);

                // ... get the rest of the URIs, ...
                var subUriList = GetSubPageList(doc, uri);

                // ... read the content of the main page, ...
                ScrapeInfo(doc);

                // ... and read the sub-pages.
                foreach (var subUri in subUriList)
                {
                    var page = GetDocumentNode(subUri);
                    ScrapeInfo(page);
                }
            }
        }

        #endregion

        #region - Private Methods -

        private HtmlNode GetDocumentNode(Uri uri)
        {
            var web = new HtmlWeb();
            var htmlDoc = web.Load(uri);

            var nodes = htmlDoc.DocumentNode;

            return nodes;
        }

        private List<Uri> GetMainPageList(HtmlNode node, Uri uri)
        {
            var nodes = node.SelectNodes("//*[@class='text11']/p/a/strong/em");

            var uris = new List<Uri>();

            foreach (var n in nodes)
            {
                var tmp = n.ParentNode.ParentNode.Attributes["href"].Value;
                uris.Add(new Uri(uri, HttpUtility.HtmlDecode(tmp)));
            }

            return uris;
        }

        private List<Uri> GetSubPageList(HtmlNode node, Uri uri)
        {
            var uriList = new List<Uri>();

            var pages = node.SelectNodes("//*[@class='pageNav']");

            if (pages != null)
            {
                foreach (var p in pages)
                {
                    var tmp = p.Attributes["href"].Value;
                    uriList.Add(new Uri(uri, HttpUtility.HtmlDecode(tmp)));
                }
            }

            return uriList;
        }

        private void ScrapeInfo(HtmlNode node)
        {
            var container = node.SelectSingleNode("//*[@class='text11']");

            int currentYear = 0;

            foreach (var child in container.ChildNodes)
            {
                var series = new StampSeries();
                if (child.OriginalName == "strong")
                {
                    var paragraphs = child.SelectNodes("//p");
                    foreach (var p in paragraphs)
                    {
                        if (p.InnerHtml.Length < 15 && p.InnerHtml.Trim().EndsWith("г."))
                        {
                            // year
                            currentYear = int.Parse(p.InnerHtml.Trim().Replace(" ", "").Replace("г.", ""));
                        }
                        else if (p.Attributes["align"] != null && p.Attributes["align"].Value == "justify")
                        {
                            series.Details += p.InnerText;
                        }
                        else if (child.OriginalName == "table")
                        {
                            break;
                        }
                    }
                }


                series.Year = currentYear;
            }


        }

        #endregion

    }
}