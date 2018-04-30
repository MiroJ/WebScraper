using Newtonsoft.Json;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Text.RegularExpressions;

namespace WebScraper
{
    public class ScraperMtitc
    {
        #region - Declarations -

        string _startUrl = @"https://www.mtitc.government.bg/archive/page.php?category=144&id=1434";

        StampSeries _currentSeries = new StampSeries {
            Stamps = new List<Stamp>()
        };

        int _currentYear;

        List<StampSeries> database = new List<StampSeries>();

        #endregion

        #region - Public methods -

        public void Start()
        {
            // Get initial page
            var homeUri = new Uri(_startUrl);
            var homePageDom = GetDocumentNode(homeUri);

            // Get all main URIs
            var uriList = GetMainPageList(homePageDom, homeUri);

            // Go to each page, ...
            foreach (var uri in uriList)
            {
                // ... load the first page, ...
                var page = GetDocumentNode(uri);

                // ... get the rest of the URIs, ...
                var subUriList = GetSubPageList(page, uri);

                // ... scrape the fisrt page, ...
                var n = page.SelectSingleNode("//td[@class='text11']");
                ScrapeContent(n);

                // ... and read the sub-pages.
                foreach (var subUri in subUriList)
                {
                    page = GetDocumentNode(subUri);
                    n = page.SelectSingleNode("//td[@class='text11']");

                    ScrapeContent(n);
                }

                var json = JsonConvert.SerializeObject(database);
                Console.WriteLine(json);
                Console.ReadKey();

                return;
            }
        }

        #endregion

        #region - Private Methods -

        private HtmlNode GetDocumentNode(Uri uri)
        {
            var web = new HtmlWeb();
            var htmlDoc = web.Load(uri);

            var node = htmlDoc.DocumentNode;

            return node;
        }

        private List<Uri> GetMainPageList(HtmlNode mainPageDom, Uri uri)
        {
            var nodes = mainPageDom.SelectNodes("//*[@class='text11']/p/a/strong/em");

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

        private void ScrapeContent(HtmlNode node)
        {
            foreach (var elem in node.ChildNodes)
            {
                if (elem.HasChildNodes)
                {
                    // Go one more level deeper
                    ScrapeContent(elem);
                }
                else // reached the last level
                {
                    if (elem.OriginalName == "table")
                    {
                        // Get photos and details of each stamp
                        AddStampsFromTable(elem);

                    }
                    else if (elem.OriginalName == "em")
                    {
                        // Get the emphasized text
                        AddComment(elem);
                    }
                    else
                    {
                        if (IsYearRow(elem.InnerHtml))
                        {
                            CloseSeries();

                            ExtractYear(elem.InnerHtml);
                        }
                        else
                        {
                            // Add the text to the description
                            AddTitleAndDescription(elem);
                        }
                    }
                }

                // Set the year, even if already set
                _currentSeries.Year = _currentYear;
            }
        }

        private void AddComment(HtmlNode elem)
        {
            _currentSeries.Comment = string.IsNullOrEmpty(_currentSeries.Comment) ? elem.InnerText : _currentSeries.Comment + "/n" + elem.InnerText;
        }

        private void AddStampsFromTable(HtmlNode elem)
        {
            var cells = elem.Descendants("td");

            var result = new List<Stamp>();
            var values = new List<(double Value, string Notes)>();

            foreach (var c in cells)
            {
                var stamp = new Stamp();

                // Get Id
                var idNodes = c.Descendants("strong");
                if (idNodes != null && idNodes.Count() > 0)
                {
                    int.TryParse(idNodes.First().InnerText, out int tmp);
                    stamp.Id = tmp;
                }

                // Get photo URL
                var photoUrls = c.Descendants("a");
                if (photoUrls != null && photoUrls.Count() > 0)
                {
                    stamp.ImageUrl = photoUrls.First().Attributes["href"].Value;
                }

                // Get value and extra note
                var valueInfo = GetStampValue(c);
                if (valueInfo.Value > 0)
                {
                    values.Add(valueInfo);
                }

                // Store the info
                if (stamp.Id > 0 && string.IsNullOrEmpty(stamp.ImageUrl) == false)
                {
                    result.Add(stamp);
                }
            }

            if (result.Count() > 0)
            {
                if(result.Count() != values.Count())
                {
                    throw new Exception("Arrays not the same size!");
                }

                for (int i = 0; i < result.Count(); i++)
                {
                    result[i].Value = values[i].Value;
                    result[i].ExtraNote = values[i].Notes;

                }
            }

            _currentSeries.Stamps.AddRange(result);
        }

        private void AddTitleAndDescription(HtmlNode elem)
        {
            var fullText = elem.InnerText.Trim();

            if (fullText.IndexOf("). Надпечатки - ") > 10 && fullText.IndexOf("). Надпечатки - ") < 25)
            {
                _currentSeries.Title = "Надпечатки";
            }
            else if (fullText.Length >= 25 && IsTitleRow(fullText.Substring(0, 6)))
            {
                var idx1 = fullText.IndexOf(@"&quot;");
                var idx2 = fullText.IndexOf(@"&quot;", idx1 + 2);

                _currentSeries.Title = fullText.Substring(idx1 + 6, idx2 - idx1 - 6);

                ExtractDate(fullText);

                fullText = fullText.Substring(idx2 + 7).Trim();
            }

            var descr = "";
            if (elem.Attributes["align"] != null && elem.Attributes["align"].Value == "justify")
            {
                descr = string.IsNullOrEmpty(descr) ? elem.InnerText : descr + "/n" + elem.InnerText;

                _currentSeries.Details = string.IsNullOrEmpty(_currentSeries.Details) ? descr : _currentSeries.Details + "\n" + descr;
            }
        }

        private void CloseSeries()
        {
            if (_currentSeries.Year > 0)
            {
                database.Add(_currentSeries);

                _currentSeries = new StampSeries
                {
                    Stamps = new List<Stamp>()
                };
            }
        }


        #endregion

        #region - Helper functions -

        private (double Value, string Notes) GetStampValue(HtmlNode cell)
        {
            var value = 0.0;
            var extraNote = "";

            foreach (var item in cell.ChildNodes)
            {
                var html = item.InnerText.Trim();

                var idx = -1;
                idx = idx == -1 ? html.IndexOf(" ст.") : idx;
                idx = idx == -1 ? html.IndexOf(" лв.") : idx;
                idx = idx == -1 ? html.IndexOf(" с.") : idx;
                idx = idx == -1 ? html.IndexOf(" фр.") : idx;

                if (idx > -1)
                {
                    var endIdx = html.IndexOf(" ", idx - 4 >= 0 ? idx - 4 : 0);

                    if (endIdx > -1)
                    {
                        idx = idx - 4 >= 0 ? idx - 4 : 0;
                        double.TryParse(html.Substring(idx, endIdx - idx), out value);
                    }
                    else
                    {
                        value = double.Parse(html.Substring(idx - 4));
                    }

                    if (value > 0)
                    {
                        endIdx = html.IndexOf(".");
                        if (endIdx < html.Length - 1)
                        {
                            extraNote = html.Substring(endIdx + 1);
                            extraNote = FixNotes(extraNote);
                        }

                        if (html.Contains(" с.") || html.Contains(" ст."))
                        {
                            value /= 100;
                        }
                    }
                }
            }

            return (value, extraNote);
        }

        private string FixNotes(string extraNote)
        {
            extraNote = extraNote.Replace(" в. ", " върху ");
            extraNote = extraNote.Replace("надп.", "надпечатка");

            return extraNote.Trim();
        }

        private void ExtractYear(string text)
        {
            _currentYear = int.Parse(text.Replace(" ", "").Replace("г.", ""));
            _currentSeries.Year = _currentYear;
        }

        private void ExtractDate(string fullText)
        {
            var year = int.Parse(fullText.Substring(0, 4));
            var month = GetMonthFromBulgarianMonth(fullText);

            var idx = fullText.IndexOf("(");

            var day = int.Parse(fullText.Substring(idx + 1, 2).Trim());

            _currentSeries.Date = new DateTime(year, month, day);
        }

        private bool IsYearRow(string text)
        {
            var pattern = @"1\s[8-9]\s[0-9]\s[0-9]\sг.";

            Regex rgx = new Regex(pattern, RegexOptions.IgnoreCase);

            MatchCollection matches = rgx.Matches(text);

            return matches.Count == 1 && matches[0].Index < 2;
        }

        private bool IsTitleRow(string text)
        {
            var pattern = @"[0-9]{4}";

            Regex rgx = new Regex(pattern, RegexOptions.IgnoreCase);

            MatchCollection matches = rgx.Matches(text);

            return matches.Count > 0;
        }

        private int GetMonthFromBulgarianMonth(string text)
        {
            text = text.ToLower();

            if (text.IndexOf("януари") > -1) return 1;
            if (text.IndexOf("февруари") > -1) return 2;
            if (text.IndexOf("март") > -1) return 3;
            if (text.IndexOf("април") > -1) return 4;
            if (text.IndexOf("май") > -1) return 5;
            if (text.IndexOf("юни") > -1) return 6;
            if (text.IndexOf("юли") > -1) return 7;
            if (text.IndexOf("август") > -1) return 8;
            if (text.IndexOf("септември") > -1) return 9;
            if (text.IndexOf("октомври") > -1) return 10;
            if (text.IndexOf("ноември") > -1) return 11;
            if (text.IndexOf("декември") > -1) return 12;

            return 0;
        }

        #endregion

    }
}