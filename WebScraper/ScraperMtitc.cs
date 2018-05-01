using Newtonsoft.Json;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;
using System.Text.RegularExpressions;
using System.IO;

namespace WebScraper
{
    public class ScraperMtitc
    {
        #region - Declarations -

        string _startUrl = @"https://www.mtitc.government.bg/bg/category/63/katalog-na-bulgarskite-poshtenski-marki-1879-2005-g";

        StampSeries _currentSeries = new StampSeries
        {
            Stamps = new List<Stamp>()
        };

        int _currentYear = 0;

        List<StampSeries> database = new List<StampSeries>();

        static int fileIndex = 6;

        #endregion

        #region - Public methods -

        public void Start()
        {
            // Get initial page
            var homeUri = new Uri(_startUrl);
            var homePageDom = GetDocumentNode(homeUri);

            // Get all main URIs
            var uriList = GetMainPageList(homePageDom, homeUri);

            Console.WriteLine("Starting...");
            Console.WriteLine($"Got {uriList.Count()} main pages.");

            uriList.Clear();
            uriList.Add(new Uri(@"https://www.mtitc.government.bg/archive/page.php?category=144&id=1863"));

            // Go to each page, ...
            foreach (var uri in uriList)
            {
                fileIndex += 1;

                Console.WriteLine($"Scraping main page...");

                // ... load the first page, ...
                var page = GetDocumentNode(uri);

                // ... get the rest of the URIs, ...
                var subUriList = GetSubPageList(page, uri);

                // ... scrape the fisrt page, ...
                var n = page.SelectSingleNode("//td[@class='text11']");
                try
                {
                    ScrapeContent(n);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in '{uri.AbsoluteUri}'");
                }
                var i = 0;
                // ... and read the sub-pages.
                foreach (var subUri in subUriList)
                {
                    i += 1;
                    Console.WriteLine($"Scraping page {i}/{subUriList.Count()} ...");

                    page = GetDocumentNode(subUri);
                    n = page.SelectSingleNode("//td[@class='text11']");

                    try
                    {
                        ScrapeContent(n);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in '{subUri.AbsoluteUri}'");
                    }
                }

                Console.WriteLine($"Completed '{uri.AbsoluteUri}'");

                WriteToJsonFile(fileIndex);
            }

            Console.WriteLine($"Data written in {fileIndex} files");
            Console.WriteLine("Done!");
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
            var links = mainPageDom.SelectNodes("//div[@class='field-items']/div/p/a/strong/em");

            var uris = new List<Uri>();

            foreach (var l in links)
            {
                var tmp = l.ParentNode.ParentNode.Attributes["href"].Value;
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
                NormalizeStupidity(elem);

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
                else if (elem.HasChildNodes && elem.FirstChild.OriginalName == "sup")
                {
                    _currentSeries.Details += " " + elem.InnerText;
                }
                else if (elem.HasChildNodes)
                {
                    // Go one more level deeper
                    ScrapeContent(elem);
                }
                else // reached the last level
                {
                    if (elem.InnerText.StartsWith("Тиражи:"))
                    {
                        AddQuantities(elem);
                    }
                    else if (elem.InnerText.Length > 25)
                    {
                        // Add the text to the description
                        AddTitleAndDescription(elem);
                    }
                }
            }
        }

        private void AddQuantities(HtmlNode elem)
        {
            _currentSeries.Quantities = elem.InnerText;
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

            if (result.Count() > 0 && result.Count() == values.Count())
            {
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
            var fullText = WebUtility.HtmlDecode(elem.InnerText.Trim());

            if (fullText.IndexOf("). Надпечатк") > 10 && fullText.IndexOf("). Надпечатк") < 25) // Надпечатки
            {
                AddDate(fullText);

                var res = AddDate(fullText);
                if (res)
                {
                    _currentSeries.Title = "Надпечатки";
                    _currentSeries.Details = string.IsNullOrEmpty(_currentSeries.Details) ? fullText : _currentSeries.Details + "\n" + fullText;
                }
            }
            else if (IsTitleRow(fullText.TrimStart().Substring(0, 5))) // starts with a year
            {
                var ch = @". ";
                var offset = ch.Length;

                var startIidx = fullText.IndexOf(ch);
                if (startIidx == -1 || startIidx > 30) // too deep
                    return;

                var endIdx = fullText.IndexOf(ch, startIidx + offset);
                if (endIdx > -1)
                {
                    while (endIdx > -1 && fullText.Substring(endIdx - 1, 2) == "г.")
                    {
                        endIdx = fullText.IndexOf(ch, endIdx + 2);
                    }
                }

                if (endIdx == -1)
                {
                    endIdx = fullText.Length;
                }

                var res = AddDate(fullText);
                if (res)
                {
                    _currentSeries.Title = WebUtility.HtmlDecode(fullText.Substring(startIidx + offset, endIdx - startIidx - offset));
                    _currentSeries.Details = WebUtility.HtmlDecode(string.IsNullOrEmpty(_currentSeries.Details) ? fullText : _currentSeries.Details + "\n" + fullText);
                }
            }
        }

        private bool AddDate(string fullText)
        {
            var idx = fullText.IndexOf("(");
            int day = 1,
                month = 1,
                year;

            if (IsDoubleYear(fullText.Replace(" ", "").Substring(0, 9)) == false)
            {
                int.TryParse(fullText.Substring(idx + 1, 2).Trim(), out day); // digits inside brackets
                month = GetMonthFromBulgarianMonth(fullText); // Bulgarian word for a month
            }

            int.TryParse(fullText.Substring(0, 4), out year); // first 4 characters

            if (year < 1879 || year > 2005)
                return false;

            if (_currentYear > 0 && _currentYear != year)
            {
                CloseSeries();
            }

            try
            {
                day = day == 0 ? 1 : day;
                month = month == 0 ? 1 : month;
                _currentSeries.Date = new DateTime(year, month, day);
            }
            catch (Exception ex)
            {
                return false;
            }

            if (_currentYear == 0)
            {
                _currentYear = year;
            }

            return true;
        }

        private void CloseSeries()
        {
            // Verify if data
            if (string.IsNullOrEmpty(_currentSeries.Title) == false)
            {
                database.Add(_currentSeries);

                _currentSeries = new StampSeries
                {
                    Stamps = new List<Stamp>()
                };

                _currentYear = 0;
            }
        }

        private void WriteToJsonFile(int index)
        {
            using (StreamWriter file = File.CreateText($@"D:\Temp\Catalog\catalog-{index}.json"))
            {
                JsonSerializer serializer = new JsonSerializer();
                //serialize object directly into file stream
                serializer.Serialize(file, database);
            }
        }

        #endregion

        #region - Helper functions -

        private void NormalizeStupidity(HtmlNode elem)
        {
            if (elem.HasChildNodes && elem.Descendants("sup").Count() > 0)
            {
                elem.InnerHtml = elem.InnerText
                                     .Replace("1/2", " 1/2");
            }
        }

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
                        double.TryParse(html.Substring(idx - 4), out value);
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

        private bool IsTitleRow(string text)
        {
            var pattern = @"[0-9]{4}\D";

            Regex rgx = new Regex(pattern, RegexOptions.IgnoreCase);

            MatchCollection matches = rgx.Matches(text);

            return matches.Count > 0;
        }

        private bool IsDoubleYear(string text)
        {
            var pattern = @"[0-9]{4}-[0-9]{4}";

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