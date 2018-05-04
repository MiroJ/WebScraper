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
        string _fileWithURLs = @"D:\Temp\Catalog\URLs.json";

        StampSeries _currentSeries = new StampSeries
        {
            Stamps = new List<Stamp>()
        };

        int _currentYear = 0;

        List<StampSeries> database = new List<StampSeries>();
        private string _exportFolder = @"D:\Temp\Catalog\";

        #endregion

        #region - Public methods -

        public void Start()
        {
            // Get initial page
            var homeUri = new Uri(_startUrl);
            var homePageDom = GetDocumentNode(_startUrl);

            // Get all main URLs
            List<string> urlList;
            if (File.Exists(_fileWithURLs))
            {
                urlList = ReadListOfUrls();
            }
            else
            {
                urlList = GetListOfPages(_startUrl);
                // Save for next time
                SaveListOfUrls(urlList);
            }

            Console.WriteLine();
            Console.WriteLine("Starting...");
            Console.WriteLine($"Needs to go to {urlList.Count()} pages.");

            // Go to each page, ...
            foreach (var url in urlList)
            {
                var idx = url.IndexOf("id=") + 3;
                var part = url.Substring(idx);

                if (part.Contains("&page="))
                {
                    part = part.Replace("&page=", "-");
                }
                else
                {
                    part += "-1";
                }

                var fileName = $"Stamps-{part}.json";

                if (File.Exists($"{_exportFolder}{fileName}"))
                {
                    Console.WriteLine($"Skipping page '{url}'");
                }
                else
                {
                    Console.WriteLine($"Scraping page '{url}'");

                    // ... load the first page, ...
                    var page = GetDocumentNode(url);

                    // ... scrape the fisrt page, ...
                    var n = page.SelectSingleNode("//td[@class='text11']");
                    try
                    {
                        ScrapeContent(n);
                        CloseSeries();

                        Console.WriteLine($"Completed.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: '{ex.Message}'");
                    }

                    SaveDataToToJsonFile(fileName);
                }

            }

            Console.WriteLine($"Data written in {urlList.Count} files");
            Console.WriteLine("Done!");

            Console.WriteLine("Press any key to finish...");
            Console.ReadKey();

        }

        public List<string> GetListOfPages(string startUrl)
        {
            // Get initial page
            var homePageDom = GetDocumentNode(startUrl);

            // Get all main URIs
            var result = GetMainPageList(homePageDom, startUrl);

            Console.WriteLine("Initial list:");
            foreach (var item in result)
            {
                Console.WriteLine(item);
            }

            var urlSubList = new List<string>();

            // Go to each page, ...
            foreach (var url in result)
            {
                Console.WriteLine($"Searching {url}");

                // ... load the first page, ...
                var page = GetDocumentNode(url);

                // ... get the rest of the URIs, ...
                var tmp = GetSubPageList(page, url);
                urlSubList.AddRange(tmp);

                Console.WriteLine($"Found {tmp.Count}.");
            }

            result.AddRange(urlSubList);

            result = result.OrderBy(x => x).ToList();

            return result;
        }

        #endregion

        #region - Private Methods -

        private HtmlNode GetDocumentNode(string url)
        {
            var uri = new Uri(url);
            var web = new HtmlWeb();
            var htmlDoc = web.Load(uri);

            var result = htmlDoc.DocumentNode;

            return result;
        }

        private List<string> GetMainPageList(HtmlNode mainPageDom, string url)
        {
            var result = new List<string>();

            var mainUri = new Uri(url);

            var links = mainPageDom.SelectNodes("//div[@class='field-items']/div/p/a/strong/em");
            foreach (var l in links)
            {
                var tmp = l.ParentNode.ParentNode.Attributes["href"].Value;
                var uri = new Uri(mainUri, HttpUtility.HtmlDecode(tmp));
                result.Add(uri.AbsoluteUri);
            }

            return result;
        }

        private List<string> GetSubPageList(HtmlNode node, string url)
        {
            var result = new List<string>();

            var mainUri = new Uri(url);

            var pages = node.SelectNodes("//*[@class='pageNav']");
            if (pages != null)
            {
                foreach (var p in pages)
                {
                    var tmp = p.Attributes["href"].Value;
                    result.Add(new Uri(mainUri, HttpUtility.HtmlDecode(tmp)).AbsoluteUri);
                }
            }

            return result;
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

        private void CloseSeries(bool force = false)
        {
            // Verify if data
            if (string.IsNullOrEmpty(_currentSeries.Title) == false || force)
            {
                database.Add(_currentSeries);

                _currentSeries = new StampSeries
                {
                    Stamps = new List<Stamp>()
                };

                _currentYear = 0;
            }
        }

        private void SaveDataToToJsonFile(string fileName)
        {
            using (StreamWriter file = File.CreateText($"{_exportFolder}{fileName}"))
            {
                JsonSerializer serializer = new JsonSerializer();
                //serialize object directly into file stream
                serializer.Serialize(file, database);
            }

            database.Clear();
        }

        private void SaveListOfUrls(List<string> urls)
        {
            using (StreamWriter file = File.CreateText(_fileWithURLs))
            {
                JsonSerializer serializer = new JsonSerializer();
                //serialize object directly into file stream
                serializer.Serialize(file, urls);
            }
        }
        private List<string> ReadListOfUrls()
        {
            var bytesArray = File.ReadAllText(_fileWithURLs);
            var urls = JsonConvert.DeserializeObject<List<string>>(bytesArray);

            return urls;
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
