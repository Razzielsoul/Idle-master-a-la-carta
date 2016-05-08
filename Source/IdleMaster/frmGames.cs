using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using IdleMaster.Properties;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace IdleMaster
{
    public partial class frmGames : Form
    {
        public List<Badge> AllGames { get; set; }

        public int RetryCount = 0;
        public int ReloadCount = 0;
        public Badge CurrentGame;

        public frmGames()
        {
            InitializeComponent();
            AllGames = new List<Badge>();
        }

        private void frmGames_Closed(object sender, EventArgs e)
        {
            if (CurrentGame != null)
            {
                CurrentGame.StopIdle();
            }
        }

        private async void frmGames_Load(object sender, EventArgs e)
        {
            // Localize form
            btnIdle.Text = localization.strings.force_idle;
            this.Text = localization.strings.search_game;
            label1.Text = localization.strings.game_to_search;
            label2.Text = localization.strings.hours;
            label2.Text = string.Empty;

            await LoadBadgesAsync();
        }

        private void btnIdle_Click(object sender, EventArgs e)
        {
            if (lstGames.SelectedItem != null)
            {
                if (CurrentGame != null)
                    CurrentGame.StopIdle();
                CurrentGame = AllGames.FirstOrDefault(b => b.Name == lstGames.SelectedItem.ToString());
                if (CurrentGame != null)
                {
                    label2.Text = @"Currently in idle: " + CurrentGame.Name;
                    CurrentGame.Idle();
                }
            }
            txtName.Text = string.Empty;
            txtName.Focus();
        }

        private void txtName_TextChanged(object sender, EventArgs e)
        {
            lstGames.Items.Clear();
            if (txtName.Text.Length > 0)
            {
                foreach (var game in AllGames)
                {
                    if (game.Name.ToUpper().Contains(txtName.Text.ToUpper()))
                        lstGames.Items.Add(game.Name);
                }
            }
        }


        public async Task LoadBadgesAsync()
        {
            // Settings.Default.myProfileURL = http://steamcommunity.com/id/USER
            var profileLink = Settings.Default.myProfileURL + "/badges";
            var pages = new List<string>() { "?p=1" };
            var document = new HtmlDocument();
            int pagesCount = 1;

            try
            {
                // Load Page 1 and check how many pages there are
                var pageURL = string.Format("{0}/?p={1}", profileLink, 1);
                var response = await CookieClient.GetHttpAsync(pageURL);
                // Response should be empty. User should be unauthorised.
                if (string.IsNullOrEmpty(response))
                {
                    RetryCount++;
                    if (RetryCount == 18)
                    {
                        return;
                    }
                    throw new Exception("");
                }
                document.LoadHtml(response);

                // If user is authenticated, check page count. If user is not authenticated, pages are different.
                var pageNodes = document.DocumentNode.SelectNodes("//a[@class=\"pagelink\"]");
                if (pageNodes != null)
                {
                    pages.AddRange(pageNodes.Select(p => p.Attributes["href"].Value).Distinct());
                    pages = pages.Distinct().ToList();
                }

                string lastpage = pages.Last().ToString().Replace("?p=", "");
                pagesCount = Convert.ToInt32(lastpage);

                // Get all badges from current page
                ProcessBadgesOnPage(document);

                // Load other pages
                for (var i = 2; i <= pagesCount; i++)
                {
                    // Load Page 2+
                    pageURL = string.Format("{0}/?p={1}", profileLink, i);
                    response = await CookieClient.GetHttpAsync(pageURL);
                    // Response should be empty. User should be unauthorised.
                    if (string.IsNullOrEmpty(response))
                    {
                        RetryCount++;
                        if (RetryCount == 18)
                        {
                            return;
                        }
                        throw new Exception("");
                    }
                    document.LoadHtml(response);

                    // Get all badges from current page
                    ProcessBadgesOnPage(document);
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Game -> LoadBadgesAsync, for profile = " + Settings.Default.myProfileURL);
                // badge page didn't load

                // Set the form height
                var graphics = CreateGraphics();
                var scale = graphics.DpiY * 1.625;
                Height = Convert.ToInt32(scale);

                ReloadCount = 1;
                return;
            }

            RetryCount = 0;
            //UpdateStateInfo();
        }

        /// <summary>
        /// Processes all badges on page
        /// </summary>
        /// <param name="document">HTML document (1 page) from x</param>
        private void ProcessBadgesOnPage(HtmlDocument document)
        {
            foreach (var badge in document.DocumentNode.SelectNodes("//div[@class=\"badge_row is_link\"]"))
            {
                var appIdNode = badge.SelectSingleNode(".//a[@class=\"badge_row_overlay\"]").Attributes["href"].Value;
                var appid = Regex.Match(appIdNode, @"gamecards/(\d+)/").Groups[1].Value;

                if (string.IsNullOrWhiteSpace(appid) || Settings.Default.blacklist.Contains(appid) || appid == "368020" || appid == "335590" || appIdNode.Contains("border=1"))
                {
                    continue;
                }

                var hoursNode = badge.SelectSingleNode(".//div[@class=\"badge_title_stats_playtime\"]");
                var hours = hoursNode == null ? string.Empty : Regex.Match(hoursNode.InnerText, @"[0-9\.,]+").Value;

                var nameNode = badge.SelectSingleNode(".//div[@class=\"badge_title\"]");
                var name = WebUtility.HtmlDecode(nameNode.FirstChild.InnerText).Trim();

                var cardNode = badge.SelectSingleNode(".//span[@class=\"progress_info_bold\"]");
                var cards = cardNode == null ? string.Empty : Regex.Match(cardNode.InnerText, @"[0-9]+").Value;

                var badgeInMemory = AllGames.FirstOrDefault(b => b.StringId == appid);
                if (badgeInMemory != null)
                {
                    badgeInMemory.UpdateStats(cards, hours);
                }
                else
                {
                    AllGames.Add(new Badge(appid, name, cards, hours));
                }
            }
        }
    }
}
