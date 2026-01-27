using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HtmlAgilityPack;
using System.IO;
using System.Net;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Text.RegularExpressions;

namespace MeetupScraper
{
    class Program
    {
        ////// Configuration - loaded from text files //////
        static List<string> cities = new List<string>();
        static List<string> keywords = new List<string>();
        static List<string> excludedWords = new List<string>();
        static List<string> excludedOrganizers = new List<string>();
        static int minAttendees = 1;
        static string emailUrl = ""; // Empty = skip email, set URL to enable
        static Dictionary<string, string> cityWithStateConversion = new Dictionary<string, string>();

        // Config file paths (relative to exe location)
        static string ConfigFolder => AppContext.BaseDirectory;
        static string KeywordsFile => Path.Combine(ConfigFolder, "keywords.txt");
        static string CitiesFile => Path.Combine(ConfigFolder, "cities.txt");
        static string ExcludedWordsFile => Path.Combine(ConfigFolder, "excluded_words.txt");
        static string ExcludedOrganizersFile => Path.Combine(ConfigFolder, "excluded_organizers.txt");
        static string CityMappingsFile => Path.Combine(ConfigFolder, "city_mappings.txt");
        static string SettingsFile => Path.Combine(ConfigFolder, "settings.txt");

        // Default values
        static readonly List<string> DefaultKeywords = new List<string>() {
            "Ambidextrous Tennis", "Ambidextrous", "Polymath", "Composer", "Musician", "Spotify",
            "Game Developer", "GameDev", "Gamers", "League of Legends", "Esports", "Starcraft",
            "Two Rackets", "Filmmaker", "Actor", "Director", "YouTuber", "YouTube", "Content Creator",
            "Anime", "Animation", "VTuber", "Autodidact", "Piano Improvisation",
            "Tennis with Two Rackets", "People Who Do Everything", "Video Games", "Animators",
            "Networking", "Startup", "Indie", "Streamer", "Twitch"
        };
        static readonly List<string> DefaultCities = new List<string>() { "Los Angeles" };
        static readonly List<string> DefaultExcludedWords = new List<string>() {
            "African", "Asian", "Brooklyn", "Flushing", "Long Island", "Mental Health", "Cardio", "Table Tennis",
            "Bronx", "Astoria", "New Jersey", "Forest Hills", "White Plains", "Class", "Workshop", "Bootcamp",
            "Training", "Board Game", "Concert", "Muslim", "Christian", "Catholic", "Jewish", "Symposium", "MMTB",
            "Ensemble", "Female", "Webinar", "Bach", "New Jersey", "NNJ", "Sausage", "Gay", "LGBT", "50+", "60+",
            "50plus", "60plus", "quartet", "feminism", "career fair", "opera", "adaptive sport", "pickle",
            "volleyball", "yonkers", "drills", "golf", "wedding", "squad", "balboa", "beatles", "recital",
            "rehearsal", "east bay", "eastbay", "real estate", "scientology", "nude", "male", "sexual"
        };
        static readonly Dictionary<string, string> DefaultCityMappings = new Dictionary<string, string>()
        {
            {"San Francisco", "us--ca--San%20Francisco"},
            {"Los Angeles", "us--ca--Los%20Angeles"},
            {"Honolulu", "us--hi--Honolulu"},
            {"London", "gb--Greater%20London--London"},
            {"Oslo", "no--Norway--Oslo"},
            {"Berlin", "de--Germany--Berlin"},
            {"Tokyo", "jp--Tokyo"},
            {"New York City", "us--ny--New%20York"},
            {"Hawaii", "us--hi--Honolulu"}
        };

        static void LoadConfig()
        {
            WriteLine("Loading configuration...");

            // Load keywords
            keywords = LoadListFromFile(KeywordsFile, DefaultKeywords, "# Keywords to search for (one per line)");

            // Load cities
            cities = LoadListFromFile(CitiesFile, DefaultCities, "# Cities to search in (one per line)");

            // Load excluded words
            excludedWords = LoadListFromFile(ExcludedWordsFile, DefaultExcludedWords, "# Words that will exclude an event if found (one per line)");

            // Load excluded organizers
            excludedOrganizers = LoadListFromFile(ExcludedOrganizersFile, new List<string>(), "# Organizer names to exclude (one per line)");

            // Load city mappings
            cityWithStateConversion = LoadDictionaryFromFile(CityMappingsFile, DefaultCityMappings,
                "# City to Meetup URL mapping (format: CityName=url_code)\n# Add mappings for cities that need special URL encoding");

            // Load settings
            LoadSettings();

            WriteLine($"  Keywords: {string.Join(", ", keywords)}");
            WriteLine($"  Cities: {string.Join(", ", cities)}");
            WriteLine($"  Excluded words: {excludedWords.Count} loaded");
            WriteLine($"  Min attendees: {minAttendees}");
        }

        static List<string> LoadListFromFile(string filePath, List<string> defaults, string header)
        {
            if (!File.Exists(filePath))
            {
                WriteLine($"  Creating {Path.GetFileName(filePath)} with defaults...");
                var lines = new List<string> { header };
                lines.AddRange(defaults);
                File.WriteAllLines(filePath, lines);
                return new List<string>(defaults);
            }

            var result = File.ReadAllLines(filePath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                .Select(line => line.Trim())
                .ToList();

            WriteLine($"  Loaded {result.Count} items from {Path.GetFileName(filePath)}");
            return result;
        }

        static Dictionary<string, string> LoadDictionaryFromFile(string filePath, Dictionary<string, string> defaults, string header)
        {
            if (!File.Exists(filePath))
            {
                WriteLine($"  Creating {Path.GetFileName(filePath)} with defaults...");
                var lines = new List<string> { header };
                lines.AddRange(defaults.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                File.WriteAllLines(filePath, lines);
                return new Dictionary<string, string>(defaults);
            }

            var result = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                    result[parts[0].Trim()] = parts[1].Trim();
            }

            WriteLine($"  Loaded {result.Count} mappings from {Path.GetFileName(filePath)}");
            return result;
        }

        static void LoadSettings()
        {
            if (!File.Exists(SettingsFile))
            {
                WriteLine($"  Creating {Path.GetFileName(SettingsFile)} with defaults...");
                var lines = new List<string>
                {
                    "# Settings (format: key=value)",
                    "minAttendees=1",
                    "",
                    "# Email URL for sending results (leave empty to disable)",
                    "# Results are sent via POST with these variables:",
                    "#   z = category (\"Events\")",
                    "#   y = subject (\"Events Alert\")",
                    "#   x = results text (the full event list)",
                    "# Example: https://example.com/email.php",
                    "emailUrl="
                };
                File.WriteAllLines(SettingsFile, lines);
                return;
            }

            foreach (var line in File.ReadAllLines(SettingsFile))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                var parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim().ToLower();
                    var value = parts[1].Trim();

                    switch (key)
                    {
                        case "minattendees":
                            if (int.TryParse(value, out int min))
                                minAttendees = min;
                            break;
                        case "emailurl":
                            emailUrl = value;
                            break;
                    }
                }
            }
        }

        ///// end of configurables //////

        static SortedDictionary<DateTime, Dictionary<string, List<Tuple<string, string>>>> foundEvents = new SortedDictionary<DateTime, Dictionary<string, List<Tuple<string, string>>>>();

        static HashSet<string> duplicateURLs = new HashSet<string>();
        static HashSet<string> duplicateEvs = new HashSet<string>();

        // Rate limiting to avoid being blocked
        static Random rng = new Random();
        static void RandomDelay(int minMs, int maxMs)
        {
            int delay = rng.Next(minMs, maxMs);
            WriteLine($"    (waiting {delay / 1000.0:F1}s)");
            System.Threading.Thread.Sleep(delay);
        }

        // Browser management with retry logic
        static int maxRetries = 3;

        static ChromeDriver CreateBrowser()
        {
            var options = new ChromeOptions();

            string chromePath64 = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
            string chromePath32 = "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe";
            if (File.Exists(chromePath64))
                options.BinaryLocation = chromePath64;
            else if (File.Exists(chromePath32))
                options.BinaryLocation = chromePath32;

            options.AddArguments("--headless=new");
            options.AddArguments("--disable-gpu");
            options.AddArguments("--no-sandbox");
            options.AddArguments("--disable-dev-shm-usage");
            options.AddArguments("--disable-software-rasterizer");
            options.AddArguments("--log-level=3");
            options.AddArguments("--silent");
            options.AddArguments("--disable-extensions");
            options.AddArguments("--disable-logging");
            options.AddArguments("--window-size=1920,1080");

            var service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            var browser = new ChromeDriver(service, options, TimeSpan.FromSeconds(90));
            browser.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
            browser.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
            browser.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(30);

            return browser;
        }

        static string SafeGetPageSource(ref ChromeDriver browser, int retries = 3)
        {
            for (int attempt = 1; attempt <= retries; attempt++)
            {
                try
                {
                    return browser.PageSource;
                }
                catch (WebDriverException ex)
                {
                    WriteLine($"    PageSource failed (attempt {attempt}/{retries}): {ex.Message}");

                    if (attempt < retries)
                    {
                        WriteLine("    Recreating browser...");
                        try { CleanupBrowser(browser); } catch { }
                        System.Threading.Thread.Sleep(3000);
                        browser = CreateBrowser();
                        System.Threading.Thread.Sleep(2000);
                    }
                }
            }
            return null; // All retries failed
        }

        static bool SafeNavigate(ref ChromeDriver browser, string url, int retries = 3)
        {
            for (int attempt = 1; attempt <= retries; attempt++)
            {
                try
                {
                    browser.Navigate().GoToUrl(url);
                    return true;
                }
                catch (WebDriverException ex)
                {
                    WriteLine($"    Navigation failed (attempt {attempt}/{retries}): {ex.Message}");

                    if (attempt < retries)
                    {
                        WriteLine("    Recreating browser...");
                        try { CleanupBrowser(browser); } catch { }
                        System.Threading.Thread.Sleep(3000);
                        browser = CreateBrowser();
                        System.Threading.Thread.Sleep(2000);
                    }
                }
            }
            return false; // All retries failed
        }

        static void WriteLine(string s)
        {
            Console.WriteLine(DateTime.Now + ": " + s);
            File.AppendAllLines("MeetupScraper.txt", new string[] { DateTime.Now + ": " + s });
        }

        static void Main(string[] args)
        {
            WriteLine("New run...");
            LoadConfig();
            try
            {
                while (true)
                {
                    // unhide to send full list each time
                    //duplicateURLs = new HashSet<string>();
                    //duplicateEvs = new HashSet<string>();
                    //foundEvents = new SortedDictionary<DateTime, Dictionary<string, List<Tuple<string, string>>>>();

                    //call the functions to search both sites
                    SearchMeetup();
                    SearchEventbrite();

                    //send an email with the gathered information attached
                    PrintResults();
                    //System.Threading.Thread.Sleep(5000);
                    System.Threading.Thread.Sleep(7*24 * 60 * 60*1000);
                }
            }
            catch (Exception e) { WriteLine("Error: " + e); }

            Console.Read();
        }

        private static void SearchMeetup()
        {
            int c = 0;
            ChromeDriver browser = null;

            try
            {
                browser = CreateBrowser();

                foreach (string keyword in keywords)
                {
                    try
                    {
                        ServicePointManager.Expect100Continue = true;
                        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;

                        foreach (string l in cities)
                        {
                            try
                            {
                                string encodedKeyword = Uri.EscapeDataString(keyword);
                                string url = "https://www.meetup.com/find/?keywords=" + encodedKeyword + "&source=EVENTS&eventType=inPerson&distance=twentyFiveMiles&location=" + (cityWithStateConversion.ContainsKey(l) ? cityWithStateConversion[l] : Uri.EscapeDataString(l));
                                WriteLine("Searching " + url);

                                if (!SafeNavigate(ref browser, url))
                                {
                                    WriteLine(" - Failed to navigate after retries, skipping...");
                                    continue;
                                }

                                RandomDelay(5000, 8000);

                                string source = SafeGetPageSource(ref browser);
                                if (source == null)
                                {
                                    WriteLine(" - Failed to get page source after retries, skipping...");
                                    continue;
                                }

                                // Scroll to load more content
                                int scrollAttempts = 0;
                                while (scrollAttempts < 5)
                                {
                                    string prevSource = SafeGetPageSource(ref browser);
                                    if (prevSource == null) break;

                                    try
                                    {
                                        IJavaScriptExecutor js = (IJavaScriptExecutor)browser;
                                        js.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                                    }
                                    catch { }

                                    RandomDelay(3000, 5000);

                                    string newSource = SafeGetPageSource(ref browser);
                                    if (newSource == null || newSource == prevSource)
                                        break;

                                    scrollAttempts++;
                                }

                                source = SafeGetPageSource(ref browser);
                                if (source == null)
                                {
                                    WriteLine(" - Failed to get page source after scrolling, skipping...");
                                    continue;
                                }
                            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                            doc.LoadHtml(source);

                            if (doc == null) continue;

                            // Find event links - try multiple selectors
                            HtmlNodeCollection curEventLinks = null;
                            
                            // Try various selectors
                            string[] selectors = new string[]
                            {
                                "//a[contains(@href, '/events/') and contains(@class, 'eventCard')]",
                                "//a[@data-testid='event-card']",
                                "//div[contains(@class, 'eventCard')]//a[contains(@href, '/events/')]",
                                "//a[contains(@href, '/events/') and @data-element-name='eventCard']"
                            };
                            
                            foreach (string selector in selectors)
                            {
                                curEventLinks = doc.DocumentNode.SelectNodes(selector);
                                if (curEventLinks != null && curEventLinks.Count > 0)
                                {
                                    WriteLine(" - Found " + curEventLinks.Count + " events with selector: " + selector);
                                    break;
                                }
                            }
                            
                            // Fallback: extract all event links
                            if (curEventLinks == null)
                            {
                                var allLinks = doc.DocumentNode.SelectNodes("//a[@href]");
                                if (allLinks != null)
                                {
                                    var eventUrls = allLinks
                                        .Select(n => n.GetAttributeValue("href", ""))
                                        .Where(href => href.Contains("/events/") && 
                                                      !href.Contains("/events/past") && 
                                                      !href.Contains("/events/calendar") &&
                                                      href.Contains("meetup.com"))
                                        .Distinct()
                                        .ToList();
                                    
                                    if (eventUrls.Any())
                                    {
                                        WriteLine(" - Found " + eventUrls.Count + " event URLs via fallback");
                                        // Process URLs directly
                                        foreach (string eventUrl in eventUrls)
                                        {
                                            ProcessEventUrl(ref browser, eventUrl, keyword, l, ref c);
                                        }
                                        continue;
                                    }
                                }
                                
                                WriteLine(" - No event cards found on search page");
                                continue;
                            }

                            foreach (HtmlNode curEventNode in curEventLinks)
                            {
                                string eventUrl = curEventNode.GetAttributeValue("href", "");
                                if (string.IsNullOrEmpty(eventUrl) || !eventUrl.Contains("/events/"))
                                    continue;
                                
                                if (!eventUrl.StartsWith("http"))
                                    eventUrl = "https://www.meetup.com" + eventUrl;

                                ProcessEventUrl(ref browser, eventUrl, keyword, l, ref c);
                            }
                            }
                            catch (Exception cityEx)
                            {
                                WriteLine(" - City error (continuing): " + cityEx.Message);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        WriteLine("Keyword error (continuing): " + e.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                WriteLine("Browser error: " + e.ToString());
            }
            finally
            {
                // Proper cleanup
                CleanupBrowser(browser);
            }
            
            WriteLine("Results from Meetup: " + c);
        }

        private static void ProcessEventUrl(ref ChromeDriver browser, string eventUrl, string keyword, string city, ref int c)
        {
            try
            {
                WriteLine(" - Checking " + eventUrl);

                string subURL = eventUrl.Split('?').First();
                if (duplicateURLs.Contains(subURL))
                    return;
                duplicateURLs.Add(subURL);

                if (!SafeNavigate(ref browser, eventUrl))
                {
                    WriteLine(" - - Navigation failed after retries");
                    return;
                }

                RandomDelay(4000, 7000);

                string source = SafeGetPageSource(ref browser);
                if (source == null)
                {
                    WriteLine(" - - Failed to get page source");
                    return;
                }

                HtmlAgilityPack.HtmlDocument doc2 = new HtmlAgilityPack.HtmlDocument();
                doc2.LoadHtml(source);

                // Read organizer
                string organizer = "";
                try
                {
                    var orgNode = doc2.DocumentNode.SelectSingleNode(".//*[contains(@class,'organizer')]//a") ??
                                  doc2.DocumentNode.SelectSingleNode(".//*[contains(@data-testid,'organizer')]") ??
                                  doc2.DocumentNode.SelectSingleNode("//a[contains(@href, '/members/')]");
                    if (orgNode != null)
                        organizer = orgNode.InnerText.Trim();
                }
                catch { }

                // Read description
                string descriptionString = "";
                try
                {
                    var descNode = doc2.DocumentNode.SelectSingleNode(".//*[contains(@class,'break-words')]") ??
                                   doc2.DocumentNode.SelectSingleNode(".//*[contains(@data-testid,'event-description')]") ??
                                   doc2.DocumentNode.SelectSingleNode("//div[contains(@class,'description')]");
                    if (descNode != null)
                        descriptionString = descNode.InnerText.ToLower();
                }
                catch { }

                // Read attendee count
                int numAttend = 1;
                try
                {
                    var attendeeText = doc2.DocumentNode.SelectSingleNode(".//*[contains(text(), 'attending') or contains(text(), 'attendee') or contains(text(), 'going')]");
                    if (attendeeText != null)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(attendeeText.InnerText, @"(\d+)\s*(attending|attendee|going)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                            numAttend = Int32.Parse(match.Groups[1].Value);
                    }
                }
                catch { }

                // Read date - prefer JSON-LD for reliability
                DateTime eventDate = DateTime.Now;
                try
                {
                    var jsonLd = doc2.DocumentNode.SelectSingleNode("//script[@type='application/ld+json']");
                    if (jsonLd != null)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(jsonLd.InnerText, @"""startDate""\s*:\s*""([^""]+)""");
                        if (match.Success)
                        {
                            eventDate = DateTime.Parse(match.Groups[1].Value);
                            WriteLine(" - - Date: " + eventDate.ToShortDateString());
                        }
                    }
                    else
                    {
                        var timeNode = doc2.DocumentNode.SelectSingleNode(".//time[@datetime]");
                        if (timeNode != null)
                        {
                            string dt = timeNode.GetAttributeValue("datetime", "");
                            if (!string.IsNullOrEmpty(dt))
                                eventDate = DateTime.Parse(dt);
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLine(" - - Date parse error: " + ex.Message);
                    return;
                }

                if (eventDate > DateTime.Now.AddMonths(3) || eventDate < DateTime.Now.AddDays(-1))
                {
                    WriteLine(" - - Date out of range: " + eventDate.ToShortDateString());
                    return;
                }

                // Read event name
                string eventName = "";
                var nameNode = doc2.DocumentNode.SelectSingleNode(".//h1") ??
                               doc2.DocumentNode.SelectSingleNode(".//*[contains(@data-testid,'event-title')]");
                if (nameNode != null)
                    eventName = nameNode.InnerText.Replace(',', ' ').Trim() + " (" + numAttend + ")";

                if (string.IsNullOrEmpty(eventName))
                {
                    WriteLine(" - - Could not find event name");
                    return;
                }

                WriteLine(" - - Event: " + eventName);

                // Check excluded words
                bool excl = false;
                foreach (string exc in excludedWords)
                {
                    if (descriptionString.Replace("tennis shoes", "").Contains(exc.ToLower()) || 
                        eventName.ToLower().Contains(exc.ToLower()) || 
                        eventUrl.Replace("-", " ").ToLower().Contains(exc.ToLower()))
                    {
                        excl = true;
                        WriteLine(" - - Excluded by: " + exc);
                        break;
                    }
                }

                if (excl) return;

                // Add if matches criteria - whole word match only for description
                bool keywordInTitle = eventName.ToLower().Contains(keyword.ToLower());
                bool keywordInDescription = Regex.IsMatch(descriptionString, @"\b" + Regex.Escape(keyword.ToLower()) + @"\b", RegexOptions.IgnoreCase);

                if (!excludedOrganizers.Contains(organizer) && numAttend >= minAttendees &&
                    (keywordInTitle || keywordInDescription))
                {
                    // Show where keyword was found
                    if (keywordInTitle)
                        WriteLine(" - - Keyword found in title: \"" + eventName + "\"");
                    if (keywordInDescription)
                        WriteLine(" - - Keyword found in description: \"" + GetSnippet(descriptionString, keyword) + "\"");

                    string k = keyword + ", " + city;
                    if (!foundEvents.ContainsKey(eventDate))
                        foundEvents[eventDate] = new Dictionary<string, List<Tuple<string, string>>>();

                    if (!foundEvents[eventDate].ContainsKey(k))
                        foundEvents[eventDate][k] = new List<Tuple<string, string>>();

                    foundEvents[eventDate][k].Add(Tuple.Create(eventName, eventUrl));
                    c++;
                    WriteLine(" - - Added #" + c);
                }
                else if (!keywordInTitle && !keywordInDescription)
                {
                    WriteLine(" - - Excluded: keyword '" + keyword + "' not found in title or description");
                }
            }
            catch (Exception ex)
            {
                WriteLine(" - - Error: " + ex.Message);
            }
        }

        private static string GetSnippet(string text, string keyword, int contextChars = 40)
        {
            try
            {
                var match = Regex.Match(text, @"\b" + Regex.Escape(keyword) + @"\b", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    int start = Math.Max(0, match.Index - contextChars);
                    int end = Math.Min(text.Length, match.Index + match.Length + contextChars);
                    string snippet = text.Substring(start, end - start);

                    // Clean up snippet
                    snippet = snippet.Replace("\n", " ").Replace("\r", " ").Replace("  ", " ").Trim();

                    // Add ellipsis if truncated
                    if (start > 0) snippet = "..." + snippet;
                    if (end < text.Length) snippet = snippet + "...";

                    return snippet;
                }
            }
            catch { }
            return "(snippet unavailable)";
        }

        private static void CleanupBrowser(ChromeDriver browser)
        {
            try
            {
                if (browser != null)
                {
                    browser.Quit();  // Quit is better than Close+Dispose
                }
            }
            catch { }

            // Kill any remaining chromedriver processes
            System.Threading.Thread.Sleep(1000);
            try
            {
                foreach (var proc in System.Diagnostics.Process.GetProcessesByName("chromedriver"))
                {
                    try { proc.Kill(); } catch { }
                }
            }
            catch { }
        }

        private static void SearchEventbrite()
        {
            int c = 0;
            ChromeDriver browser = null;

            try
            {
                browser = CreateBrowser();

                foreach (string keyword in keywords)
                {
                    foreach (string city in cities)
                    {
                        try
                        {
                            string citySlug = city.Replace(" ", "-").ToLower();
                            string encodedKeyword = Uri.EscapeDataString(keyword);
                            string url = $"https://www.eventbrite.com/d/{citySlug}/{encodedKeyword}/";
                            WriteLine("Searching EventBrite: " + url);

                            if (!SafeNavigate(ref browser, url))
                            {
                                WriteLine(" - Failed to navigate after retries, skipping...");
                                continue;
                            }

                            RandomDelay(5000, 8000);

                            // Scroll to load more events
                            for (int scroll = 0; scroll < 3; scroll++)
                            {
                                try
                                {
                                    ((IJavaScriptExecutor)browser).ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                                    RandomDelay(2000, 4000);
                                }
                                catch { }
                            }

                            string source = SafeGetPageSource(ref browser);
                            if (source == null)
                            {
                                WriteLine(" - Failed to get page source after retries, skipping...");
                                continue;
                            }

                            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                            doc.LoadHtml(source);

                            // Find event links - EventBrite uses various structures
                            var eventUrls = new HashSet<string>();

                            // Method 1: Look for links containing /e/ (event pages)
                            var allLinks = doc.DocumentNode.SelectNodes("//a[@href]");
                            if (allLinks != null)
                            {
                                foreach (var link in allLinks)
                                {
                                    string href = link.GetAttributeValue("href", "");
                                    if (href.Contains("eventbrite.com/e/") || href.StartsWith("/e/"))
                                    {
                                        if (href.StartsWith("/"))
                                            href = "https://www.eventbrite.com" + href;
                                        href = href.Split('?').First();
                                        eventUrls.Add(href);
                                    }
                                }
                            }

                            WriteLine($" - Found {eventUrls.Count} event links");

                            foreach (string eventUrl in eventUrls)
                            {
                                ProcessEventbriteUrl(ref browser, eventUrl, keyword, city, ref c);
                            }
                        }
                        catch (Exception e)
                        {
                            WriteLine("EventBrite search error: " + e.Message);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                WriteLine("EventBrite browser error: " + e.ToString());
            }
            finally
            {
                CleanupBrowser(browser);
            }

            WriteLine("Results from EventBrite: " + c);
        }

        private static void ProcessEventbriteUrl(ref ChromeDriver browser, string eventUrl, string keyword, string city, ref int c)
        {
            try
            {
                string subURL = eventUrl.Split('?').First();
                if (duplicateURLs.Contains(subURL))
                    return;
                duplicateURLs.Add(subURL);

                WriteLine(" - Checking " + subURL);

                if (!SafeNavigate(ref browser, eventUrl))
                {
                    WriteLine(" - - Navigation failed after retries");
                    return;
                }

                RandomDelay(4000, 7000);

                string source = SafeGetPageSource(ref browser);
                if (source == null)
                {
                    WriteLine(" - - Failed to get page source");
                    return;
                }

                HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(source);

                // Get event name from h1 or title
                string eventName = "";
                var h1Node = doc.DocumentNode.SelectSingleNode("//h1");
                if (h1Node != null)
                    eventName = h1Node.InnerText.Trim();
                else
                {
                    var titleNode = doc.DocumentNode.SelectSingleNode("//title");
                    if (titleNode != null)
                        eventName = titleNode.InnerText.Split('|').First().Trim();
                }

                if (string.IsNullOrEmpty(eventName))
                {
                    WriteLine(" - - Could not find event name");
                    return;
                }

                WriteLine(" - - Event: " + eventName);

                // Get description - try multiple selectors
                string descriptionString = "";
                string[] descSelectors = new string[]
                {
                    "//*[contains(@class,'event-description')]",
                    "//*[contains(@class,'eds-text')]",
                    "//div[contains(@class,'description')]",
                    "//section[contains(@class,'event-details')]"
                };

                foreach (var selector in descSelectors)
                {
                    var descNode = doc.DocumentNode.SelectSingleNode(selector);
                    if (descNode != null && descNode.InnerText.Length > 50)
                    {
                        descriptionString = descNode.InnerText.ToLower();
                        break;
                    }
                }

                // Fallback: get main content area
                if (string.IsNullOrEmpty(descriptionString))
                {
                    var mainNode = doc.DocumentNode.SelectSingleNode("//main") ?? doc.DocumentNode.SelectSingleNode("//body");
                    if (mainNode != null)
                        descriptionString = mainNode.InnerText.ToLower();
                }

                // Get organizer
                string organizer = "";
                var orgNode = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'organizer')]//a") ??
                              doc.DocumentNode.SelectSingleNode("//*[contains(@class,'organizer-name')]");
                if (orgNode != null)
                    organizer = orgNode.InnerText.Trim();

                // Get date from JSON-LD or meta tags
                DateTime eventDate = DateTime.Now;
                bool foundDate = false;

                // Try JSON-LD first
                var jsonLdNode = doc.DocumentNode.SelectSingleNode("//script[@type='application/ld+json']");
                if (jsonLdNode != null)
                {
                    var match = Regex.Match(jsonLdNode.InnerText, @"""startDate""\s*:\s*""([^""]+)""");
                    if (match.Success)
                    {
                        try
                        {
                            eventDate = DateTime.Parse(match.Groups[1].Value);
                            foundDate = true;
                        }
                        catch { }
                    }
                }

                // Try meta tags
                if (!foundDate)
                {
                    var metaNode = doc.DocumentNode.SelectSingleNode("//meta[@property='event:start_time']") ??
                                   doc.DocumentNode.SelectSingleNode("//meta[@name='date']");
                    if (metaNode != null)
                    {
                        try
                        {
                            eventDate = DateTime.Parse(metaNode.GetAttributeValue("content", ""));
                            foundDate = true;
                        }
                        catch { }
                    }
                }

                if (!foundDate)
                {
                    WriteLine(" - - Could not parse date, using today");
                }
                else
                {
                    WriteLine(" - - Date: " + eventDate.ToShortDateString());
                }

                // Check date range
                if (eventDate > DateTime.Now.AddMonths(3))
                {
                    WriteLine(" - - Date out of range (too far future): " + eventDate.ToShortDateString());
                    return;
                }

                if (eventDate < DateTime.Now.AddDays(-1))
                {
                    WriteLine(" - - Date out of range (past): " + eventDate.ToShortDateString());
                    return;
                }

                // Check city
                if (!source.ToLower().Contains(city.ToLower()))
                {
                    WriteLine(" - - Wrong city, not in " + city);
                    return;
                }

                eventName = eventName.Replace(',', ' ').Trim();

                // Check excluded words
                bool excl = false;
                foreach (string exc in excludedWords)
                {
                    if (descriptionString.Replace("tennis shoes", "").Contains(exc.ToLower()) ||
                        eventName.ToLower().Contains(exc.ToLower()))
                    {
                        excl = true;
                        WriteLine(" - - Excluded by: " + exc);
                        break;
                    }
                }

                if (excl) return;

                // Check keyword match - whole word only for description
                bool keywordInTitle = eventName.ToLower().Contains(keyword.ToLower());
                bool keywordInDescription = Regex.IsMatch(descriptionString, @"\b" + Regex.Escape(keyword.ToLower()) + @"\b", RegexOptions.IgnoreCase);

                if (!excludedOrganizers.Contains(organizer) && (keywordInTitle || keywordInDescription))
                {
                    if (keywordInTitle)
                        WriteLine(" - - Keyword found in title: \"" + eventName + "\"");
                    if (keywordInDescription)
                        WriteLine(" - - Keyword found in description: \"" + GetSnippet(descriptionString, keyword) + "\"");

                    string k = keyword + ", " + city + " (EventBrite)";
                    if (!foundEvents.ContainsKey(eventDate))
                        foundEvents[eventDate] = new Dictionary<string, List<Tuple<string, string>>>();

                    if (!foundEvents[eventDate].ContainsKey(k))
                        foundEvents[eventDate][k] = new List<Tuple<string, string>>();

                    foundEvents[eventDate][k].Add(Tuple.Create(eventName, subURL));
                    c++;
                    WriteLine(" - - Added #" + c);
                }
                else if (!keywordInTitle && !keywordInDescription)
                {
                    WriteLine(" - - Excluded: keyword '" + keyword + "' not found in title or description");
                }
            }
            catch (Exception ex)
            {
                WriteLine(" - - Error: " + ex.Message);
            }
        }

        private static void PrintResults()
        {
            try
            {
                WriteLine("Results: ");
                string results = CreateCSV();
                WriteLine(results);
                if (results != "")
                {
                    // Save to results.txt (append with date prefix)
                    SaveResultsToFile(results);

                    // Send email (only if URL is configured)
                    if (!string.IsNullOrEmpty(emailUrl))
                    {
                        try
                        {
                            WriteLine("Sending results to: " + emailUrl);
                            using (WebClient client = new WebClient())
                            {
                                byte[] response =
                                client.UploadValues(emailUrl, new System.Collections.Specialized.NameValueCollection()
                                {
                                    { "z", "Events" },
                                    { "y", "Events Alert"},
                                    { "x", results }
                                });
                            }
                            WriteLine("Email sent successfully.");
                        }
                        catch (Exception emailEx)
                        {
                            WriteLine("Email failed (results still saved to file): " + emailEx.Message);
                        }
                    }
                    else
                    {
                        WriteLine("Email URL not configured, skipping email notification.");
                    }
                }
            }
            catch (Exception exc)
            {
                WriteLine(exc.ToString());
            }
        }

        private static void SaveResultsToFile(string results)
        {
            try
            {
                string resultsFile = Path.Combine(ConfigFolder, "results.txt");
                string dateHeader = "\n" + new string('=', 60) + "\n" +
                                   DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - Search Results\n" +
                                   new string('=', 60) + "\n\n";
                File.AppendAllText(resultsFile, dateHeader + results + "\n");
                WriteLine("Results saved to: " + resultsFile);
            }
            catch (Exception ex)
            {
                WriteLine("Failed to save results to file: " + ex.Message);
            }
        }

        private static string CreateCSV()
        {
            try
            {
                int i = 1;
                //create a string to hold the data file
                var csv = new StringBuilder();

                foreach (KeyValuePair<DateTime, Dictionary<string, List<Tuple<string, string>>>> foundEvent in foundEvents)
                    {
                    foreach(KeyValuePair<string, List<Tuple<string, string>>> keyw in foundEvent.Value) {
                        foreach (Tuple<string, string> ev in keyw.Value)
                        {
                            string eve = foundEvent.Key.ToShortDateString() + " (" + keyw.Key + "): " + ev.Item1 + " \n - " + ev.Item2;
                            if (duplicateEvs.Contains(eve)) continue;
                            csv.Append(i + ". " + eve + " \n\n");
                            duplicateEvs.Add(eve);
                            i++;
                        }
                    }
                }

                

                return csv.ToString();
            }
            catch (Exception exc)
            {
                return exc.ToString();
            }
        }
    }
}
