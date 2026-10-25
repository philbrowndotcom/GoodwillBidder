// GoodwillPicker — logs into shopgoodwill, reads your favorited items to build a
// "taste profile", then ranks TODAY's closing auctions (optionally within a
// category) by how well they match that profile. Recommend-only: it never bids.
//
// Usage:
//   dotnet run --project GoodwillPicker -- [options]
// Options:
//   --category=<id>   Restrict search to this category id (skips the menu). If
//                     omitted, you pick from a menu of your favorite categories.
//   --auto            Skip the menu; search your top favorite categories.
//   --favtype=all     Which favorites to learn from: open | close | all (default all).
//   --days=<n>        Include auctions closing within N days (default 0 = today only).
//   --top=<n>         How many recommendations to print (default 20).
//   --pages=<n>       Search pages to scan per category, 40 items each (default 8).
//   --sort=<n>        sortColumn value (default "1"); --sortdesc to reverse.
//   --maxfav=<n>      Cap how many favorites to fetch detail for (default 80).
//   --html            Also write an HTML page of the picks and open it in a browser.
//   --out=<path>      Where to write the HTML (default: temp file with a timestamp).
//   --no-open         With --html, write the file but don't launch the browser.

using System.Diagnostics;
using GoodwillPicker;
using GoodwillPicker.Models;

string GetOpt(string name, string def)
{
    foreach (var a in args)
        if (a.StartsWith($"--{name}=", StringComparison.OrdinalIgnoreCase))
            return a[(name.Length + 3)..];
    return def;
}
bool HasFlag(string name) => Array.Exists(args, a => string.Equals(a, $"--{name}", StringComparison.OrdinalIgnoreCase));

int categoryArg = int.TryParse(GetOpt("category", ""), out var c) ? c : 0;
string favType   = GetOpt("favtype", "all").ToLowerInvariant();
int days         = int.TryParse(GetOpt("days", "0"), out var d) ? Math.Max(0, d) : 0;
int top          = int.TryParse(GetOpt("top", "20"), out var t) ? Math.Max(1, t) : 20;
int pages        = int.TryParse(GetOpt("pages", "8"), out var pg) ? Math.Clamp(pg, 1, 50) : 8;
int maxFav       = int.TryParse(GetOpt("maxfav", "80"), out var mf) ? Math.Clamp(mf, 1, 500) : 80;
string sortCol   = GetOpt("sort", "1");
bool sortDesc    = HasFlag("sortdesc");
bool autoSelect  = HasFlag("auto"); // skip the interactive category menu
bool htmlOut     = HasFlag("html"); // also write an HTML page and open it
bool noOpen      = HasFlag("no-open"); // with --html, write the file but don't launch a browser
string outPath   = GetOpt("out", ""); // custom HTML output path
const int pageSize = 40;

using var cli = Sgw.NewClient();

Console.WriteLine("Logging in...");
string token = await Sgw.LoginAsync(cli);
Console.WriteLine("Logged in.");

// --- 1. Read favorites ---
Console.WriteLine($"Fetching favorites (type={favType})...");
var favIds = await Sgw.GetFavoriteItemIdsAsync(cli, token, favType);
Console.WriteLine($"Found {favIds.Count} favorited item(s).");
if (favIds.Count == 0)
{
    Console.WriteLine("No favorites to learn from. Heart some items on shopgoodwill first, then re-run.");
    return;
}

// --- 2. Fetch detail per favorite to learn titles/keywords/categories ---
var toFetch = favIds.Take(maxFav).ToList();
if (favIds.Count > maxFav)
    Console.WriteLine($"(Using the first {maxFav} of {favIds.Count} favorites; raise with --maxfav.)");

var favInfos = new List<ItemInfo>();
TimeSpan serverOffset = TimeSpan.Zero;
bool haveOffset = false;
Console.Write("Learning from favorites: ");
foreach (var id in toFetch)
{
    try
    {
        var reqAt = DateTime.Now;
        var (info, serverTime) = await Sgw.GetItemDetailAsync(cli, id);
        if (info is not null) favInfos.Add(info);
        // Anchor "today" to the server's clock (shopgoodwill reports times in its
        // own zone) using the first detail response's serverTime.
        if (!haveOffset && serverTime is DateTime st)
        {
            var mid = reqAt + (DateTime.Now - reqAt) / 2;
            serverOffset = st - mid;
            haveOffset = true;
        }
        Console.Write(".");
    }
    catch { Console.Write("x"); }
}
Console.WriteLine();

if (favInfos.Count == 0)
{
    Console.WriteLine("Could not load any favorite details; cannot build a profile.");
    return;
}

var profile = Sgw.BuildProfile(favInfos);
Console.WriteLine($"Profile: {profile.Keywords.Count} keyword(s) across {profile.Categories.Count} category(ies).");

DateTime ServerNow() => DateTime.Now + serverOffset;
if (haveOffset)
    Console.WriteLine($"Server clock offset {serverOffset.TotalMinutes:0.#} min; using server time for the close-date window.");

// Close-date window: from now through end of (today + days), in server time.
DateTime windowEnd = ServerNow().Date.AddDays(days + 1).AddSeconds(-1);

// --- 3. Decide which categories to search ---
string CatName(int id) => profile.CategoryNames.TryGetValue(id, out var n) && n.Length > 0 ? n : $"category {id}";

// Favorite categories, most-favorited first.
var favCats = profile.Categories.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();

List<int> categories;
if (categoryArg > 0)
{
    // Explicit id always wins, no prompt.
    categories = new List<int> { categoryArg };
    Console.WriteLine($"Searching {CatName(categoryArg)} ({categoryArg}).");
}
else if (favCats.Count == 0)
{
    categories = new List<int> { 0 }; // no category info -> search everything
    Console.WriteLine("No category info from favorites; searching all listings.");
}
else if (autoSelect)
{
    // Non-interactive: hunt the top favorite categories.
    categories = favCats.Take(5).ToList();
    Console.WriteLine($"--auto: searching top favorite categories: {string.Join(", ", categories.Select(CatName))}.");
}
else
{
    categories = SelectCategories(favCats, profile);
}

// Interactive menu: show favorite categories (name + count) and let the user pick.
static List<int> SelectCategories(List<int> favCats, FavProfile profile)
{
    var menu = favCats.Take(20).ToList();
    Console.WriteLine();
    Console.WriteLine("Your favorite categories:");
    for (int i = 0; i < menu.Count; i++)
    {
        int id = menu[i];
        string name = profile.CategoryNames.TryGetValue(id, out var n) && n.Length > 0 ? n : $"category {id}";
        int count = (int)profile.Categories[id];
        Console.WriteLine($"  {i + 1,2}) [{id,6}] {name}  ({count} favorite{(count == 1 ? "" : "s")})");
    }
    Console.WriteLine();
    Console.Write("Pick number(s), comma-separated (Enter or 'a' = all shown): ");
    string? line = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(line) || line.Trim().Equals("a", StringComparison.OrdinalIgnoreCase))
        return menu;

    var picked = new List<int>();
    foreach (var tok in line.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        if (int.TryParse(tok, out var n) && n >= 1 && n <= menu.Count && !picked.Contains(menu[n - 1]))
            picked.Add(menu[n - 1]);

    if (picked.Count == 0)
    {
        Console.WriteLine("No valid selection; using all shown.");
        return menu;
    }
    return picked;
}

// --- 4. Scan current auctions and keep those closing within the window ---
Console.WriteLine($"Scanning up to {pages} page(s) x {pageSize} per category for auctions closing by {windowEnd:g} (server)...");
var seen = new HashSet<int>();
var candidates = new List<ItemInfo>();
int scanned = 0;
foreach (var cat in categories)
{
    for (int page = 1; page <= pages; page++)
    {
        List<ItemInfo> pageItems;
        try { pageItems = await Sgw.SearchPageAsync(cli, token, cat, page, pageSize, sortCol, sortDesc); }
        catch (Exception ex) { Console.WriteLine($"  search error (cat {cat}, page {page}): {ex.Message}"); break; }

        if (pageItems.Count == 0) break;
        scanned += pageItems.Count;

        foreach (var it in pageItems)
        {
            if (profile.FavoriteItemIds.Contains(it.ItemId)) continue; // skip things already favorited
            if (!seen.Add(it.ItemId)) continue;
            if (it.EndTime is DateTime et && et <= windowEnd && et >= ServerNow().AddMinutes(-1))
                candidates.Add(it);
        }
    }
}
Console.WriteLine($"Scanned {scanned} listing(s); {candidates.Count} close within the window.");

// --- 5. Rank against the favorites profile (TF-IDF) and print recommendations ---
var ranked = Sgw.Rank(candidates, profile).Take(top).ToList();

Console.WriteLine();
string windowLabel = days == 0 ? "today" : $"within {days + 1} day(s)";
if (ranked.Count == 0)
{
    Console.WriteLine("No matching auctions closing in the window. Try --days=1, more --pages, or a --category.");
}
else
{
    Console.WriteLine($"=== Top {ranked.Count} pick(s) closing {windowLabel} ===");
    int rank = 1;
    foreach (var r in ranked)
    {
        var it = r.Item;
        DateTime? localClose = it.EndTime is DateTime et ? et - serverOffset : null;
        Console.WriteLine();
        Console.WriteLine($"{rank,2}. [{r.Score:0.#}] {it.Title}");
        Console.WriteLine($"    ${it.CurrentPrice:0.00}  |  {it.NumberOfBids} bid(s)  |  closes {(localClose?.ToString("g") ?? "?")} local  |  {CatName(it.CategoryId)}");
        if (r.MatchedTerms.Count > 0)
            Console.WriteLine($"    matches: {string.Join(", ", r.MatchedTerms.Take(8))}");
        Console.WriteLine($"    https://shopgoodwill.com/item/{it.ItemId}");
        Console.WriteLine($"    snipe: dotnet run --project GoodwillBidder -- {it.ItemId} <maxBid>");
        rank++;
    }
}

// --- 6. Optional HTML report + browser launch ---
if (htmlOut)
{
    string subtitle = $"{ranked.Count} pick(s) closing {windowLabel} in {string.Join(", ", categories.Select(CatName))}";
    string html = Report.Build(ranked, profile, subtitle, DateTime.Now, serverOffset);

    string path = string.IsNullOrWhiteSpace(outPath)
        ? Path.Combine(Path.GetTempPath(), $"goodwill-picks-{DateTime.Now:yyyyMMdd-HHmmss}.html")
        : Path.GetFullPath(outPath);
    await File.WriteAllTextAsync(path, html);
    Console.WriteLine($"\nHTML report written to {path}");

    if (!noOpen)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Console.WriteLine($"Could not launch browser automatically: {ex.Message}"); }
    }
}
