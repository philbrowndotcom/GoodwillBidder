using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GoodwillPicker.Models;

namespace GoodwillPicker
{
    /// <summary>
    /// Thin client for the (unofficial) shopgoodwill buyer API, plus the
    /// keyword/category scoring used to rank today's auctions against favorites.
    /// Responses whose exact field names aren't documented are read defensively
    /// (multiple candidate keys) so the tool degrades gracefully rather than
    /// throwing when the API shape drifts.
    /// </summary>
    public static class Sgw
    {
        public const string ApiBase = "https://buyerapi.shopgoodwill.com";

        public static HttpClient NewClient()
        {
            var cli = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            cli.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) GoodwillPicker/1.0");
            return cli;
        }

        public static async Task<string> LoginAsync(HttpClient cli)
        {
            // Pre-encrypted login payload, read from credentials.json (shared with the bidder).
            using var body = new StringContent(LoginConfig.Load().ToLoginBody(), Encoding.UTF8, "application/json");
            var resp = await cli.PostAsync($"{ApiBase}/api/SignIn/Login", body);
            var json = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();

            var token = JsonSerializer.Deserialize<TokenRoot>(json);
            if (token is null || string.IsNullOrEmpty(token.accessToken))
                throw new InvalidOperationException($"Login failed: {token?.message ?? json}");
            return token.accessToken;
        }

        /// <summary>
        /// GET the ids of the user's favorited items. <paramref name="type"/> is
        /// "open" (still active), "close" (ended), or "all".
        /// </summary>
        public static async Task<List<int>> GetFavoriteItemIdsAsync(HttpClient cli, string token, string type)
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post, $"{ApiBase}/api/Favorite/GetAllFavoriteItemsByType?Type={type}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var resp = await cli.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(json);
            var ids = new List<int>();
            if (TryGetArray(doc.RootElement, out var data, "data", "favorites", "items"))
            {
                foreach (var el in data.EnumerateArray())
                {
                    int id = GetInt(el, 0, "itemId", "ItemId", "itemID", "id");
                    if (id > 0) ids.Add(id);
                }
            }
            return ids.Distinct().ToList();
        }

        /// <summary>Fetch full detail for one item, plus the server's clock stamp.</summary>
        public static async Task<(ItemInfo? item, DateTime? serverTime)> GetItemDetailAsync(HttpClient cli, int itemId)
        {
            var url = $"{ApiBase}/api/ItemDetail/GetItemDetailModelByItemId/{itemId}";
            var resp = await cli.GetAsync(url);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return (null, null);

            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var info = new ItemInfo(
                ItemId: GetInt(r, itemId, "itemId"),
                Title: GetString(r, "", "title"),
                Keywords: GetString(r, "", "keyWords", "keywords"),
                CategoryId: GetInt(r, 0, "categoryId", "catId"),
                EndTime: GetDate(r, "endTime"),
                CurrentPrice: GetDouble(r, 0, "currentPrice"),
                NumberOfBids: GetInt(r, 0, "numberOfBids", "numBids"),
                CategoryName: GetCategoryPath(r));
            return (info, GetDate(r, "serverTime"));
        }

        /// <summary>
        /// One page of a category search. Set <paramref name="categoryId"/> to 0 to
        /// search everything. Returns the parsed rows for the page.
        /// </summary>
        public static async Task<List<ItemInfo>> SearchPageAsync(
            HttpClient cli, string token, int categoryId, int page, int pageSize, string sortColumn, bool sortDescending)
        {
            var query = new Dictionary<string, object?>
            {
                ["isSize"] = false,
                ["isWeddingCatagory"] = "false",
                ["isMultipleCategoryIds"] = false,
                ["isFromHeaderMenuTab"] = false,
                ["layout"] = "",
                ["searchText"] = "",
                ["selectedGroup"] = "",
                ["selectedCategoryIds"] = categoryId > 0 ? categoryId.ToString() : "",
                ["selectedSellerIds"] = "",
                ["lowPrice"] = "0",
                ["highPrice"] = "999999",
                ["searchBuyNowOnly"] = "",
                ["searchPickupOnly"] = "false",
                ["searchNoPickupOnly"] = "false",
                ["searchOneCentShippingOnly"] = "false",
                ["searchDescriptions"] = "false",
                ["searchClosedAuctions"] = "false",
                ["closedAuctionEndingDate"] = "1/1/1970",
                ["closedAuctionDaysBack"] = "0",
                ["searchCanadaShipping"] = "false",
                ["searchInternationalShippingOnly"] = "false",
                ["sortColumn"] = sortColumn,
                ["page"] = page.ToString(),
                ["pageSize"] = pageSize.ToString(),
                ["sortDescending"] = sortDescending,
                ["savedSearchId"] = 0,
                ["useBuyerPrefs"] = "true",
                ["searchUSOnlyShipping"] = "false",
                ["categoryLevelNo"] = "1",
                ["categoryLevel"] = 1,
                ["categoryId"] = categoryId,
                ["partNumber"] = "",
                ["catIds"] = categoryId > 0 ? categoryId.ToString() : "",
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/api/Search/ItemListing");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(query), Encoding.UTF8, "application/json");

            var resp = await cli.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(json);

            // Items live under searchResults.items on the current API; fall back to
            // a top-level array if the shape ever changes.
            JsonElement itemsEl;
            if (doc.RootElement.TryGetProperty("searchResults", out var sr) &&
                TryGetArray(sr, out var arr, "items"))
            {
                itemsEl = arr;
            }
            else if (TryGetArray(doc.RootElement, out var arr2, "items", "data"))
            {
                itemsEl = arr2;
            }
            else
            {
                return new List<ItemInfo>();
            }

            var results = new List<ItemInfo>();
            foreach (var el in itemsEl.EnumerateArray())
            {
                int id = GetInt(el, 0, "itemId", "itemID", "id");
                if (id <= 0) continue;
                results.Add(new ItemInfo(
                    ItemId: id,
                    Title: GetString(el, "", "title", "itemTitle", "name"),
                    Keywords: GetString(el, "", "keyWords", "keywords"),
                    CategoryId: GetInt(el, categoryId, "categoryId", "catId"),
                    EndTime: GetDate(el, "endTime", "endTimeString", "endtime"),
                    CurrentPrice: GetDouble(el, 0, "currentPrice", "minimumBid", "currentprice"),
                    NumberOfBids: GetInt(el, 0, "numberOfBids", "numBids", "bidCount"),
                    CategoryName: "",
                    ImageUrl: NormalizeImage(GetString(el, "", "imageUrl", "imageURL", "imageUrlString", "thumbnailUrlString", "image"))));
            }
            return results;
        }

        // --- Profile building + scoring ---

        private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
        {
            "the","and","for","with","this","that","from","new","one","two","set","lot","pcs","pc",
            "size","used","vtg","vintage","not","are","was","has","have","your","you","all","see",
            "inch","inches","approx","condition","please","item","items","great","nice","good",
            "more","assorted","untested","bundle","edition","original","misc","various",
            // listing boilerplate that carries no taste signal in any category
            "etc","see","photo","photos","read","description","ships","shipping","free","brand",
            "unused","preowned","pre","owned","excellent","working","works","tested","untest",
        };

        public static IEnumerable<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            var sb = new StringBuilder();
            foreach (var ch in text)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
                else if (sb.Length > 0) { var t = sb.ToString(); sb.Clear(); if (Keep(t)) yield return t; }
            }
            if (sb.Length > 0) { var t = sb.ToString(); if (Keep(t)) yield return t; }
        }

        private static bool Keep(string t) => t.Length >= 3 && !Stop.Contains(t) && !t.All(char.IsDigit);

        /// <summary>Build the taste profile from favorited items' titles/keywords/categories.</summary>
        public static FavProfile BuildProfile(IEnumerable<ItemInfo> favorites)
        {
            var kw = new Dictionary<string, double>();
            var cat = new Dictionary<int, double>();
            var names = new Dictionary<int, string>();
            var ids = new HashSet<int>();
            foreach (var f in favorites)
            {
                ids.Add(f.ItemId);
                if (f.CategoryId > 0)
                {
                    cat[f.CategoryId] = cat.GetValueOrDefault(f.CategoryId) + 1;
                    if (!string.IsNullOrWhiteSpace(f.CategoryName))
                        names[f.CategoryId] = f.CategoryName;
                }
                // Count each term once per favorite (document frequency), so terms
                // common across many favorites outweigh a single verbose title.
                foreach (var t in Tokenize($"{f.Title} {f.Keywords}").Distinct())
                    kw[t] = kw.GetValueOrDefault(t) + 1;
            }
            return new FavProfile(kw, cat, names, ids);
        }

        /// <summary>
        /// Rank candidates by how well their *distinctive* terms match the favorites
        /// profile (TF-IDF against the candidate set). Terms that are common across
        /// the candidates — e.g. "vinyl"/"records" when you're browsing the Records
        /// category — carry almost no weight, so ranking is driven by the traits that
        /// actually separate your favorites (artists, genres, eras, labels). Terms in
        /// more than half the candidates are dropped outright as category-defining.
        /// An item that only matches generic terms scores 0 and is filtered out.
        /// </summary>
        public static List<Candidate> Rank(IReadOnlyList<ItemInfo> candidates, FavProfile p)
        {
            if (candidates.Count == 0) return new List<Candidate>();

            var tokensPer = candidates.Select(it => Tokenize($"{it.Title} {it.Keywords}").ToHashSet()).ToList();

            // Group by category so "common" is judged within a category cohort: "vinyl"
            // is ubiquitous among Records and must be discounted there, even if the
            // overall scan also pulled in other categories that dilute its frequency.
            var groups = Enumerable.Range(0, candidates.Count)
                .GroupBy(i => candidates[i].CategoryId);

            var ranked = new List<Candidate>(candidates.Count);
            foreach (var g in groups)
            {
                var idx = g.ToList();
                int n = idx.Count;

                var df = new Dictionary<string, int>();
                foreach (var i in idx)
                    foreach (var t in tokensPer[i])
                        df[t] = df.GetValueOrDefault(t) + 1;

                bool useThreshold = n >= 8; // skip the "too common" cut on tiny cohorts
                foreach (var i in idx)
                {
                    double score = 0;
                    var contrib = new List<(string term, double weight)>();
                    foreach (var t in tokensPer[i])
                    {
                        if (!p.Keywords.TryGetValue(t, out var favCount)) continue;   // not a favorite trait
                        int freq = df[t];
                        if (useThreshold && freq > n * 0.35) continue;                // category-defining, ignore
                        double idf = Math.Log((double)n / freq);                      // ->0 as a term nears ubiquity
                        // Dampen favorite frequency (log) so a distinctive trait beats a
                        // merely ubiquitous one instead of raw counts dominating.
                        double tf = 1 + Math.Log(favCount);
                        double w = tf * idf;
                        if (w <= 0) continue;
                        score += w;
                        contrib.Add((t, w));
                    }
                    var matched = contrib.OrderByDescending(c => c.weight).Select(c => c.term).ToList();
                    ranked.Add(new Candidate(candidates[i], score, matched));
                }
            }

            return ranked
                .Where(r => r.Score > 0)
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Item.EndTime)
                .ToList();
        }

        /// <summary>Turn a search row's image value into an absolute URL. Full URLs
        /// pass through; bare paths get the shopgoodwill image CDN prefix. A wrong
        /// guess just yields a broken img that the page hides via onerror.</summary>
        private static string NormalizeImage(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            // The API returns Windows-style paths (backslashes); URLs need forward slashes.
            raw = raw.Replace('\\', '/');
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return raw;
            return "https://shopgoodwillimages.azureedge.net/production/" + raw.TrimStart('/');
        }

        /// <summary>Build a readable "A &gt; B &gt; C" category path from the item
        /// detail's breadcrumb list (ordered by depth).</summary>
        private static string GetCategoryPath(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("categoryBreadCrumbs", out var bc) &&
                bc.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<(int level, string name)>();
                foreach (var el in bc.EnumerateArray())
                {
                    var name = GetString(el, "", "name", "shortName");
                    if (!string.IsNullOrWhiteSpace(name))
                        parts.Add((GetInt(el, 0, "level"), name));
                }
                if (parts.Count > 0)
                    return string.Join(" > ", parts.OrderBy(p => p.level).Select(p => p.name));
            }
            return "";
        }

        // --- Defensive JSON accessors ---

        private static bool TryGetArray(JsonElement obj, out JsonElement array, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (obj.ValueKind == JsonValueKind.Object &&
                    obj.TryGetProperty(k, out var el) && el.ValueKind == JsonValueKind.Array)
                {
                    array = el;
                    return true;
                }
            }
            array = default;
            return false;
        }

        private static bool TryProp(JsonElement obj, string[] keys, out JsonElement value)
        {
            if (obj.ValueKind == JsonValueKind.Object)
                foreach (var k in keys)
                    if (obj.TryGetProperty(k, out value)) return true;
            value = default;
            return false;
        }

        private static string GetString(JsonElement obj, string fallback, params string[] keys)
        {
            if (TryProp(obj, keys, out var v))
            {
                if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? fallback;
                if (v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                    return v.ToString();
            }
            return fallback;
        }

        private static int GetInt(JsonElement obj, int fallback, params string[] keys)
        {
            if (TryProp(obj, keys, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
                if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var j)) return j;
            }
            return fallback;
        }

        private static double GetDouble(JsonElement obj, double fallback, params string[] keys)
        {
            if (TryProp(obj, keys, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
                if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var e)) return e;
            }
            return fallback;
        }

        private static DateTime? GetDate(JsonElement obj, params string[] keys)
        {
            if (TryProp(obj, keys, out var v))
            {
                if (v.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(v.GetString(), out var d)) return d;
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var epoch))
                    return DateTimeOffset.FromUnixTimeMilliseconds(epoch).DateTime;
            }
            return null;
        }
    }
}
