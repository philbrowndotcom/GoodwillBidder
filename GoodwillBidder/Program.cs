// See https://aka.ms/new-console-template for more information

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using GoodwillBidder.Models;

// Get itemId and maxBid from command line args or prompt user
int itemId;
int maxBid;

// Positional args are itemId then maxBid; flags (--something) can appear anywhere.
var positional = Array.FindAll(args, a => !a.StartsWith("--"));

if (positional.Length < 2)
{
    Console.Write("Enter Item ID: ");
    while (!int.TryParse(Console.ReadLine(), out itemId) || itemId <= 0)
    {
        Console.Write("Invalid input. Enter a valid Item ID: ");
    }

    Console.Write("Enter Max Bid: ");
    while (!int.TryParse(Console.ReadLine(), out maxBid) || maxBid <= 0)
    {
        Console.Write("Invalid input. Enter a valid Max Bid: ");
    }
}
else
{
    itemId = int.Parse(positional[0]);
    maxBid = int.Parse(positional[1]);
}

// --dryrun (any position): run the full flow (fetch, login, timing, bid decision)
// against the live auction but skip the actual PlaceBid POST.
bool dryRun = Array.Exists(args, a => string.Equals(a, "--dryrun", StringComparison.OrdinalIgnoreCase));
if (dryRun)
{
     Console.WriteLine("*** DRY RUN: timing + bid logic only, no real bid will be placed. ***");   
}

// --simclose=<seconds>: ignore the real close time and pretend the auction closes
// <seconds> from now, so the full sleep -> wake -> fetch -> bid sequence can be
// watched quickly against any item (even a closed one). Best paired with --dryrun.
int simCloseSec = 0;
foreach (var a in args)
    if (a.StartsWith("--simclose=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a.AsSpan("--simclose=".Length), out int s))
    {
        simCloseSec = s;
    }  

// Load credentials up front so a missing/bad credentials.json fails now, not
// after sleeping until the auction is about to close.
LoginConfig login = LoginConfig.Load();

HttpClient cli;
TimeSpan timeToClose;
DateTime endTime;
int sellerId;
// Offset between this PC's clock and the auction server's clock, measured from
// the server's `serverTime` field. ServerNow() = DateTime.Now + serverOffset is
// our best estimate of the server's wall clock, which is the only clock that
// decides whether a bid landed in time.
TimeSpan serverOffset = TimeSpan.Zero;
string logFile = $"log_{itemId}.log";

string url = $"https://buyerapi.shopgoodwill.com/api/ItemDetail/GetItemDetailModelByItemId/{itemId}";

DateTime ServerNow() => DateTime.Now + serverOffset;

// How long until the auction closes, relative to the SERVER's clock (negative = the call landed after close).
string TimeToClose()
{
    var rem = endTime - ServerNow();
    return rem.TotalSeconds >= 0
        ? $"{rem.TotalSeconds:0.00}s before close"
        : $"{-rem.TotalSeconds:0.00}s after close";
}

using (cli = new HttpClient())
{

    var t0 = DateTime.Now;
    Root response = await BidSvc.GetItemAsync(cli, url);
    // Best estimate of the local time at the instant the server stamped serverTime
    // is the midpoint of the request's round trip.
    var localMid = t0 + (DateTime.Now - t0) / 2;
    sellerId = response.sellerId;
    // Anchor to the server's own clock. endTime and serverTime are reported in the
    // same (server) zone, so endTime - ServerNow() is the true time remaining --
    // no manual PT->local hour offset and no trust in this PC's clock accuracy.
    serverOffset = response.serverTime - localMid;
    endTime = response.endTime;
    Console.WriteLine($"Server clock offset {serverOffset.TotalMilliseconds:0}ms (this PC is {(serverOffset.TotalMilliseconds >= 0 ? "behind" : "ahead of")} the server).");

    if (simCloseSec > 0)
    {
        endTime = ServerNow().AddSeconds(simCloseSec);
        Console.WriteLine($"*** SIM: overriding close to {simCloseSec}s from now ({endTime:T}). ***");
    }

    timeToClose = (endTime - ServerNow());

    Console.WriteLine($"Item {itemId}: {response.title}");
    // endTime is in the server's wall-clock zone; serverOffset = serverClock - localClock,
    // so subtracting it re-expresses the close time in THIS PC's local time.
    DateTime localEndTime = endTime - serverOffset;
    Console.WriteLine($"Bidding ends at {localEndTime.ToShortTimeString()} on {localEndTime.ToShortDateString()} (your local time). Current high bid is ${response.currentPrice}. Max bid is ${maxBid}");

    // Bail if the auction is already over; otherwise sleep until ~10s before close
    // (skip the sleep entirely if less than 10s remain) and log in.
    if (timeToClose.TotalMilliseconds <= 0)
    {
        Console.WriteLine($"Auction already closed ({TimeToClose()}). Nothing to bid on.");
        return;
    }
    int preSleepMs = (int)timeToClose.TotalMilliseconds - 10000;
    if (preSleepMs > 0)
    {
        Thread.Sleep(preSleepMs);        
    }
}

using (cli = new HttpClient())
{
    HttpContent cont = new StringContent(login.ToLoginBody(),
           Encoding.UTF8,
           "application/json");
    var sw = Stopwatch.StartNew();
    var resp = await cli.PostAsync("https://buyerapi.shopgoodwill.com/api/SignIn/Login", cont);
    sw.Stop();
    Console.WriteLine($"Login completed in {sw.ElapsedMilliseconds} ms");
    var contentStr = await resp.Content.ReadAsStringAsync();

    var a = JsonSerializer.Deserialize<TokenRoot>(contentStr)!;

    var token = a.accessToken;

    // Wait until 3 seconds before bidding ends, get current bid

    double loginFactor = 0.5;  // add 50% of login time as a latency cushion
    // Two phases so the slow price fetch is OFF the critical path:
    //   Phase A: wake ~5s+ out, fetch the price, decide the amount.
    //   Phase B: sleep again, then fire the bid. The server accepts on RECEIPT
    //            time, so what matters is the request arriving before close, not
    //            when the (often slow) response comes back. PlaceBid has been seen
    //            to take ~3s round-trip under end-of-auction load, so we fire ~4s
    //            out: the bid is received well before close even if the reply lags.
    int bidLeadMs   = Math.Clamp(4000 + (int)(sw.ElapsedMilliseconds * 1.0), 3500, 12000);
    // Price check must always run BEFORE the bid, with room for a slow fetch, so
    // derive it from the bid lead rather than clamping the two independently.
    int priceLeadMs = bidLeadMs + Math.Clamp(1500 + (int)(sw.ElapsedMilliseconds * loginFactor), 1000, 6000);

    // --- Phase A: price check (early) ---
    timeToClose = (endTime - ServerNow());
    int preFetchMs = (int)timeToClose.TotalMilliseconds - priceLeadMs;
    Console.WriteLine($"At login: {timeToClose.TotalSeconds:0.00}s to close. Price-check lead {priceLeadMs}ms, sleeping {Math.Max(0, preFetchMs)}ms.");
    if (preFetchMs > 0)
    {
        Thread.Sleep(preFetchMs);
    }

    Console.WriteLine($"Fetching latest price at {TimeToClose()}...");
    var fetchSw = Stopwatch.StartNew();
    var ft0 = DateTime.Now;
    Root response = await BidSvc.GetItemAsync(cli, url);
    fetchSw.Stop();
    // Refine the clock offset now that we're close to the deadline (skip in sim
    // mode, where endTime is synthetic and anchored to the original offset).
    if (simCloseSec == 0)
    {        
        serverOffset = response.serverTime - (ft0 + (DateTime.Now - ft0) / 2);
    }
    Console.WriteLine($"Price fetch took {fetchSw.ElapsedMilliseconds}ms; now {TimeToClose()}.");

    double price = response.currentPrice;

    // Start at our max bid, but if the current price is already above our max,
    // bump the bid by 5% so we stay in the running.
    double bidAmount = maxBid;
    bool added5Percent = false;

    if (price > maxBid)
    {
        bidAmount = Math.Ceiling(maxBid * 1.05);
        added5Percent = true;
        Console.WriteLine($"Current price ${price} is above max bid ${maxBid}. Bumping 5% (rounded up) to ${bidAmount:0.00}.");
    }

    cli.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    //cont.Headers.Add("Authorization", $"Bearer {token}");

    // --- Phase B: fire the bid (final seconds) ---
    timeToClose = (endTime - ServerNow());
    int preBidMs = (int)timeToClose.TotalMilliseconds - bidLeadMs;
    if (preBidMs > 0)
    {
        Console.WriteLine($"Price decided. Sleeping {preBidMs}ms to fire at ~{bidLeadMs}ms before close.");
        Thread.Sleep(preBidMs);
    }

    // Place the bid.
    Console.WriteLine($"Sending bid ${bidAmount:0.00} at {TimeToClose()}...");
    var bidSw = Stopwatch.StartNew();
    contentStr = await BidSvc.PlaceBidAsync(cli, itemId, sellerId, bidAmount, dryRun);
    bidSw.Stop();
    Console.WriteLine($"Bid ${bidAmount:0.00} round-trip {bidSw.ElapsedMilliseconds}ms, landed {TimeToClose()}. Response: {contentStr}");

    // If we got outbid and haven't already added the 5%, add it and try one last rebid.
    if (!added5Percent && contentStr.Contains("already been outbid", StringComparison.OrdinalIgnoreCase))
    {
        bidAmount = Math.Ceiling(maxBid * 1.05);
        Console.WriteLine($"Outbid. Adding 5% and rebidding at ${bidAmount:0.00}.");

        bidSw.Restart();
        contentStr = await BidSvc.PlaceBidAsync(cli, itemId, sellerId, bidAmount, dryRun);
        bidSw.Stop();
        Console.WriteLine($"Rebid ${bidAmount:0.00} round-trip {bidSw.ElapsedMilliseconds}ms, landed {TimeToClose()}. Response: {contentStr}");
    }

    Console.ReadLine();
}

public static class BidSvc
{
    public static async Task<string> PlaceBidAsync(HttpClient cli, int itemId, int sellerId, double bidAmount, bool dryRun)
    {
        var bid = new BidRequest
        {
            itemId = itemId,
            quantity = 1,
            sellerId = sellerId,
            bidAmount = bidAmount.ToString("0.00")
        };

        var json = JsonSerializer.Serialize(bid);

        if (dryRun)
        {
            return $"[DRY RUN] no bid sent. Would POST: {json}";
        }

        var cont = new StringContent(json,
             Encoding.UTF8,
             "application/json");

        var resp = await cli.PostAsync("https://buyerapi.shopgoodwill.com/api/ItemBid/PlaceBid", cont);
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<Root> GetItemAsync(HttpClient cli, string url)
    {
        var resp = await cli.GetAsync(url);

        string contentStr = await resp.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<Root>(contentStr)!;
    }
}

