using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HtmlAgilityPack;
using System.IO;
using System.Net.Mail;
using System.Net.Mime;
using System.Net;
using System.Windows.Forms;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace MeetupScraper
{
    class Program
    {

        ////// configure below //////

        // edit this to choose the city
        static List<string> cities = new List<string>() { 
            "San Francisco", 
            //"Los Angeles", 
            //"New York City", 
            //"London", 
            //"Oslo", 
            //"Berlin", 
            //"Tokyo",
            //"Hawaii"
        };

        static List<string> keywords = new List<string>() { "Investors", "VC", "Ambidextrous Tennis", "Ambidextrous", "Polymath", "Composer", "Game Developer", "Indie", "Gamers", "League of Legends", "Esports", "Starcraft",  "Two Rackets", "Filmmaker", "Improv", "Swing Dance", "YouTuber", "Content Creator", "Music Jam", "VTuber", "Afterparty", "After-party" };
       // static List<string> keywords = new List<string>() { "Ambidextrous Tennis", "Ambidextrous", "Polymath", "Autodidact", "Piano Improvisation", "Tennis with Two Rackets", "People Who Do Everything" };
        static List<string> excludedWords = new List<string>() {// "African", "Asian", "Brooklyn", "Flushing", "Long Island", "Mental Health", "Cardio", "Table Tennis", "Bronx", "Astoria", "New Jersey", "Forest Hills", "White Plains", "Class", "Workshop", "Bootcamp", "Training", "Board Game" , "Concert", "Muslim", "Christian", "Catholic", "Jewish",  "Symposium", "MMTB", "Ensemble", "Female", "Webinar", "Bach", "New Jersey", "NNJ", "Sausage", "Gay", "LGBT", "50+", "60+", "50plus", "60plus", "quartet", "feminism", "career fair", "opera", "adaptive sport", "pickle", "volleyball", "yonkers", "drills", "golf", "wedding", "squad", "balboa", "beatles", "recital", "rehearsal", "east bay", "eastbay", "real estate", "scientology", "nude", "male"
        };
        static int minAttendees = 1;
        static List<string> excludedOrganizers = new List<string>() { };

        // convert city names to state and country for Meetup
        static Dictionary<string, string> cityWithStateConversion = new Dictionary<string, string>()
        {
            {"San Francisco", "us--ca--San%20Francisco"},
            {"Los Angeles", "us--ca--Los%20Angeles"},
            {"Honolulu", "us--hi--Honolulu"},
            {"London", "gb--Greater%20London--London"},
            {"Oslo", "no--Norway--Oslo"},
            {"Berlin", "de--Germany--Berlin"},
            {"Tokyo", "jp--Tokyo"}
        };

        ///// end of configurables //////

        static SortedDictionary<DateTime, Dictionary<string, List<Tuple<string, string>>>> foundEvents = new SortedDictionary<DateTime, Dictionary<string, List<Tuple<string, string>>>>();

        static HashSet<string> duplicateURLs = new HashSet<string>();
        static HashSet<string> duplicateEvs = new HashSet<string>();

        static void WriteLine(string s)
        {
            Console.WriteLine(DateTime.Now + ": " + s);
            File.AppendAllLines("MeetupScraper.txt", new string[] { DateTime.Now + ": " + s });
        }

        [STAThread]
        static void Main(string[] args)
        {
            WriteLine("New run...");
            try
            {
                while (true)
                {
                    duplicateURLs = new HashSet<string>();
                    duplicateEvs = new HashSet<string>();
                    foundEvents = new SortedDictionary<DateTime, Dictionary<string, List<Tuple<string, string>>>>();
                    //call the functions to search both sites
                    SearchMeetup();
                    //SearchEventbrite();

                    //send an email with the gathered information attached
                    PrintResults();
                    //System.Threading.Thread.Sleep(5000);
                    System.Threading.Thread.Sleep(24 * 60 * 60*1000);
                }
            }
            catch (Exception e) { WriteLine("Error: " + e); }

            Console.Read();
        }

        private static void SearchMeetup()
        {

            //return;

            int c = 0;

            var options = new ChromeOptions()
            {
                BinaryLocation = "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe"
            };

            options.AddArguments(new List<string>() { "headless", "disable-gpu", " --disable-gpu", "silent", "--silent", "log-level=3" });
            options.AddArgument("no-sandbox");
            var browser = new ChromeDriver(options);
            foreach (string keyword in keywords)
            {

                try
                {
                    //set security type in order to connect to secure site
                    ServicePointManager.Expect100Continue = true;
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;

                    foreach (string l in cities)
                    {
                        //load the keyword in meetup. This will only get the first 20 results, as this site loads dynamically
                        HtmlWeb web = new HtmlWeb();
                        string url = "https://www.meetup.com/find/?keywords=" + keyword + "&source=EVENTS&eventType=inPerson&distance=twentyFiveMiles&location=" + (cityWithStateConversion.ContainsKey(l) ? cityWithStateConversion[l] : l).Replace(" ", "%20");
                        WriteLine("Searching " + url);

                        browser.Navigate().GoToUrl(url);
                        System.Threading.Thread.Sleep(2 * 1000);
                        string source = browser.PageSource;
                        int i = 0;
                        do
                        {
                            source = browser.PageSource;
                            IJavaScriptExecutor js = (IJavaScriptExecutor)browser;
                            js.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                            System.Threading.Thread.Sleep(2 * 1000);
                        }
                        while (browser.PageSource != source && i<5);
                        HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                        doc.LoadHtml(source);
                        
                       

                        if (doc == null) continue;

                        //make a list of all the events on the page
                        HtmlNodeCollection curEventLinks = doc.DocumentNode.SelectNodes("//*[@id=\'event-card-in-search-results']");
                        if (curEventLinks == null) continue;
                        foreach (HtmlNode curEventNode in curEventLinks)
                        {
                            try
                            {
                                

                                WriteLine(" - Checking " + curEventNode.Attributes[1].Value.ToString());
                                //WriteLine(curEventNode.InnerHtml);
                                //find the name of the event organizer
                                string organizer = "";
                                /*try
                                {
                                    organizer = curEventNode.SelectSingleNode(".//*[contains(@class,'hidden text-gray6 md:line-clamp-1')]").InnerText.ToString().Replace("Group name:", "").Trim();
                                    WriteLine(" - - Organizer: " + organizer.ToString());
                                    //organizer = organizer.Substring(19, organizer.Length - (organizer.Length - organizer.IndexOf("•")) - 20);
                                }
                                catch(Exception ex) { WriteLine(" - - Failed to read organizer: "+ex); }*/
                                //find the number of event attendees
                                /*WriteLine(" - - Reading attendees...");
                                string numString = curEventNode.SelectSingleNode(".//*[contains(@class,'text-sm text-gray6')]").InnerText.ToString().Trim();
                                numString = numString.Split(' ').First();
                                WriteLine(" - - Attendees: " + numString.ToString());*/
                                int numAttend = 1; // Int32.Parse(numString);

                                
                                string subURL = curEventNode.Attributes[1].Value.ToString().Split('?').First() + "?attending=" + numAttend;
                                if (duplicateURLs.Contains(subURL))
                                {
                                    continue;
                                }
                                else duplicateURLs.Add(subURL);
                                

                                //get the url of the event page and load it
                                string eventUrl = curEventNode.Attributes[1].Value.ToString();
                                //HtmlAgilityPack.HtmlDocument doc2 = web.Load(eventUrl);

                                browser.Navigate().GoToUrl(eventUrl);
                                System.Threading.Thread.Sleep(2 * 1000);
                                source = browser.PageSource;
                                i = 0;
                                do
                                {
                                    source = browser.PageSource;
                                    IJavaScriptExecutor js = (IJavaScriptExecutor)browser;
                                    js.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                                    System.Threading.Thread.Sleep(2 * 1000);
                                }
                                while (browser.PageSource != source && i < 5);
                                HtmlAgilityPack.HtmlDocument doc2 = new HtmlAgilityPack.HtmlDocument();
                                doc2.LoadHtml(source);


                                WriteLine(" - - Reading organizer..." + (source.Contains("text-sm font-medium leading-5") ? " Found." : ""));
                                try
                                {
                                    organizer = doc2.DocumentNode.SelectSingleNode(".//*[contains(@class,'text-sm font-medium leading-5')]").InnerText;
                                    WriteLine(" - - Organizer: " + organizer);
                                }
                                catch { }

                                WriteLine(" - - Reading description..."+(source.Contains("break-words")? " Found." :""));
                                string descriptionString = "";
                                try
                                {
                                    descriptionString = doc2.DocumentNode.SelectSingleNode(".//*[contains(@class,'break-words')]").InnerText.ToLower();
                                }
                                catch { }
                                WriteLine(" - - Reading new attendees... ");
                                try
                                {
                                    numAttend = Math.Min(numAttend, doc2.DocumentNode.SelectNodes(".//*[contains(@data-event-label,'event-attendee')]").Count);
                                    WriteLine(" - - Attendees: " + numAttend);
                                }
                                catch { }

                                //get the description of the event, so it can be checked for the keyword
                                
                                string dateString = doc2.DocumentNode.SelectSingleNode(".//time").InnerText;
                                dateString = dateString.Substring(dateString.IndexOf(", ") + 2, dateString.IndexOf(" at ") - 8);
                                dateString = dateString.Replace(" at", "");
                                dateString = dateString.Replace(" a", "");
                                WriteLine(" - - Reading date... '" + dateString+"'");
                                DateTime eventDate = DateTime.Parse(dateString);

                                if (eventDate > DateTime.Now.AddMonths(1))
                                {
                                    WriteLine(" - - Too far in the future.");
                                    continue;
                                }
                                if (eventDate < DateTime.Now.AddDays(-1))
                                {
                                    WriteLine(" - - Date is in the past.");
                                    continue;
                                }

                                //get the name of the event
                                WriteLine(" - - Reading event name...");
                                string eventName = doc2.DocumentNode.SelectSingleNode(".//h1").InnerText.Replace(',', ' ') + " (" + numAttend + ")";
                                WriteLine(" - - Event name: " + eventName);

                                bool excl = false;
                                WriteLine(" - - Checking excluded words...");
                                foreach (string exc in excludedWords)
                                    if (descriptionString.Replace("tennis shoes", "").Contains(exc.ToLower()) || eventName.ToLower().Contains(exc.ToLower()) || eventUrl.Replace("-", " ").ToLower().Contains(exc.ToLower()))
                                    {
                                        if (exc.ToLower().Contains("beginner") && (descriptionString.Contains("advanced") || descriptionString.Contains("intermediate") || descriptionString.Split(new string[] { exc.ToLower() }, StringSplitOptions.None).Where(x => x.Split(new string[] { ".", "!", "?" }, StringSplitOptions.None).Last().Contains(" not ")).Any()))
                                        {

                                        }
                                        else if (exc.ToLower().Contains("beginner") && (eventName.Contains("advanced") || eventName.Contains("intermediate") || eventName.Split(new string[] { exc.ToLower() }, StringSplitOptions.None).Where(x => x.Split(new string[] { ".", "!", "?" }, StringSplitOptions.None).Last().Contains(" not ")).Any()))
                                        {

                                        }
                                        else
                                        {
                                            excl = true;
                                            WriteLine(" - excluded by " + exc);
                                            break;
                                        }
                                    }

                                if (excl) continue;

                                //check if the event meets the given requirements and add it to the final list if it does
                                if (!excludedOrganizers.Contains(organizer) && numAttend >= minAttendees && (descriptionString.Contains(keyword.ToLower()) || eventName.ToLower().Contains(keyword.ToLower()) || eventUrl.Replace("-", " ").ToLower().Contains(keyword.ToLower())))
                                {
                                    string k = keyword + ", " + l;
                                    //if the key already exists, add to its list. Otherwise add a key for this date with a new list
                                    if (!foundEvents.ContainsKey(eventDate))
                                        foundEvents[eventDate] = new Dictionary<string, List<Tuple<string, string>>>();

                                    if (!foundEvents[eventDate].ContainsKey(k))
                                        foundEvents[eventDate][k] = new List<Tuple<string, string>>();

                                    foundEvents[eventDate][k].Add(Tuple.Create(eventName, eventUrl));

                                    

                                    c++;

                                    WriteLine(" - - Added #" + c + " " + eventName);
                                }
                                else WriteLine(" - Keyword "+keyword+" not found in description, url, or event name " + eventName);
                            }
                            catch (Exception ex) { WriteLine(" - - " + ex); }
                        }
                    }
                }
                catch (Exception e)
                { WriteLine(e.ToString()); }
            }
            browser.Close();
            browser.Dispose();
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("chromedriver.exe"))
            {
                proc.Kill();
            }
            WriteLine("Results from Meetup: " + c);

        }

        private static void SearchEventbrite()
        {
            int c = 0;
            foreach (string keyword in keywords)
            {

                try
                {
                    //set security type in order to connect to secure site
                    ServicePointManager.Expect100Continue = true;
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;

                    foreach (string l in cities)
                    {
                        //search for the keyword on eventbrite, setting the location to the entire US
                        HtmlWeb web = new HtmlWeb();
                        string url = "https://eventbrite.com/d/"+l.Replace(" ", "-").ToLower()+"/" + keyword;
                        WriteLine("Searching " + url);
                        HtmlAgilityPack.HtmlDocument doc = web.Load(url);

                        if (doc == null)
                        {
                             WriteLine("Empty.");
                            continue;
                        }

                        //find out how many pages of results there are
                        string pageCountString = doc.DocumentNode.SelectSingleNode(".//*[contains(@class,'eds-pagination__results')]").InnerText;
                        int start = pageCountString.IndexOf("of ") + 3;
                        int end = pageCountString.IndexOf("Next");
                        int length = pageCountString.Length;
                        pageCountString = pageCountString.Substring(start, length - end - 8);
                        int pageCount = Int32.Parse(pageCountString);

                        //loop through all the pages of results
                        for (int j = 1; j < pageCount + 1; j++)
                        {
                            try
                            {
                                WriteLine("Eventbrite " + keyword + " page " + j + "/" + pageCount);
                                //find all the events on this page
                                HtmlNodeCollection curEventLinks = doc.DocumentNode.SelectNodes("//*[contains(@class,'event-card-link')]");

                                //loop through the events on this page
                                foreach (HtmlNode curLink in curEventLinks)
                                {
                                    string subURL = curLink.Attributes[0].Value.ToString().Split('?').First();
                                    if (duplicateURLs.Contains(subURL)) continue;
                                    else duplicateURLs.Add(subURL);

                                    WriteLine(" - Checking " + subURL);
                                    //load the page for the current event
                                    HtmlAgilityPack.HtmlDocument doc2 = web.Load(subURL);

                                    string organizer = "";

                                    try
                                    {
                                        //find the event organizer
                                        organizer = doc2.DocumentNode.SelectSingleNode("//*[contains(@class,'organizer-info__name')]").InnerText;
                                        organizer = organizer.Substring(3, organizer.Length - 3);
                                    }
                                    catch (Exception exc)
                                    { WriteLine(exc.ToString()); }

                                    string eventName = "";
                                    try
                                    {
                                        //find the name of the event
                                        eventName = doc2.DocumentNode.SelectSingleNode("//*[contains(@class,'event-title')]").InnerText;
                                    }
                                    catch (Exception exc)
                                    {
                                        try
                                        {
                                            //sometimes the event name is under a different class
                                            eventName = doc2.DocumentNode.SelectSingleNode("//*[contains(@class,'listing-title')]").InnerText;
                                        }
                                        catch (Exception e)
                                        { Console.WriteLine(e.ToString()); }
                                    }

                                    WriteLine(" - - " + eventName);

                                    //get the description of the event, so it can be checked for the keyword
                                    string descriptionString = doc2.DocumentNode.SelectSingleNode("//*[contains(@class,'structured-content-rich-text')]").InnerText.ToLower();
                                    //WriteLine(" - - " + descriptionString);

                                    //get the date of the event
                                    string dateString = "";
                                    try
                                    {
                                        //dateString = doc2.DocumentNode.SelectSingleNode("//*[contains(@class,'js-date-time-first-line')]").InnerText;
                                        //dateString = dateString.Substring(dateString.IndexOf(", ") + 2, dateString.Length - dateString.IndexOf(", ") - 2);
                                        dateString = doc2.DocumentNode.SelectSingleNode("//body").InnerText.Substring(doc2.DocumentNode.SelectSingleNode("//body").InnerText.IndexOf("\"displayDate\":\"") + 15, 100);
                                        dateString = dateString.Substring(dateString.IndexOf(", ")+2, dateString.Length - dateString.IndexOf(", ") - 2);
                                        if(dateString.Contains(" at")) dateString = dateString.Substring(0, dateString.IndexOf(" at"));
                                        if(dateString.Contains(" from")) dateString = dateString.Substring(0, dateString.IndexOf(" from"));
                                        WriteLine("Parsing date: " + dateString);
                                    }
                                    catch
                                    {
                                        WriteLine(" - - Failed date " + dateString);
                                    }
                                    DateTime eventDate = DateTime.Parse(dateString);

                                    if (eventDate > DateTime.Now.AddMonths(1))
                                    {
                                        WriteLine(" - - Too far in the future.");
                                        continue;
                                    }

                                    if (eventDate < DateTime.Now.AddDays(-1))
                                    {
                                        WriteLine(" - - Date is in the past.");
                                        continue;
                                    }

                                    if (!doc2.DocumentNode.OuterHtml.Contains(l.ToLower()))
                                    {
                                        WriteLine(" - - Wrong city, not in "+l);
                                        continue;
                                    }

                                    //get the url of the event
                                    string eventUrl = subURL;

                                    eventName.Replace(',', ' ');

                                    bool excl = false;
                                    foreach (string exc in excludedWords)
                                        if (descriptionString.Replace("tennis shoes", "").Contains(exc.ToLower()) || eventName.ToLower().Contains(exc.ToLower()) || eventUrl.Replace("-", " ").ToLower().Contains(exc.ToLower()))
                                        {
                                            if (exc.ToLower().Contains("beginner") && (descriptionString.Contains("advanced") || descriptionString.Contains("intermediate") || descriptionString.Split(new string[] { exc.ToLower() }, StringSplitOptions.None).Where(x => x.Split(new string[] { ".", "!", "?" }, StringSplitOptions.None).Last().Contains(" not ")).Any()))
                                            {

                                            }
                                            else if (exc.ToLower().Contains("beginner") && (eventName.Contains("advanced") || eventName.Contains("intermediate") || eventName.Split(new string[] { exc.ToLower() }, StringSplitOptions.None).Where(x => x.Split(new string[] { ".", "!", "?" }, StringSplitOptions.None).Last().Contains(" not ")).Any()))
                                            {

                                            }
                                            else
                                            {
                                                excl = true;
                                                WriteLine(" - excluded by " + exc);
                                                break;
                                            }
                                        }

                                    if (excl) continue;

                                    //if the event isn't by one of the excluded organizers, add it to the list
                                    if (!excludedOrganizers.Contains(organizer) && (descriptionString.Contains(keyword.ToLower()) || eventName.ToLower().Contains(keyword.ToLower()) || eventUrl.Replace("-", " ").ToLower().Contains(keyword.ToLower())))
                                    {
                                        string k = keyword + ", " + l;
                                        //if the key already exists, add to its list. Otherwise add a key for this date with a new list
                                        if (!foundEvents.ContainsKey(eventDate))
                                            foundEvents[eventDate] = new Dictionary<string, List<Tuple<string, string>>>();

                                        if (!foundEvents[eventDate].ContainsKey(k))
                                            foundEvents[eventDate][k] = new List<Tuple<string, string>>();

                                        foundEvents[eventDate][k].Add(Tuple.Create(eventName, eventUrl));

                                        c++;
                                    }
                                    else WriteLine(" - Keyword " + keyword + " not found in description, url, or event name " + eventName);
                                }

                                doc = web.Load("https://eventbrite.com/d/united-states/" + keyword + "/?page=" + (j + 1));
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception e)
                {
                    WriteLine(e.ToString());
                }
            }
            WriteLine("Results from Eventbrite: " + c);
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
                    using (WebClient client = new WebClient())
                    {

                        byte[] response =
                        client.UploadValues("https://www.pftq.com/stocks/TechTrader/email_generic.php", new System.Collections.Specialized.NameValueCollection()
       {
       { "z", "Events" } ,   
       {"y", "Events Alert"},
       { "x", results }
       });

                    }
                }
            }
            catch (Exception exc)
            {
                WriteLine(exc.ToString());
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
