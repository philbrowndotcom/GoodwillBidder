# GoodwillBidder

Console tools for [shopgoodwill.com](https://shopgoodwill.com) auctions, built on the site's unofficial buyer API (`https://buyerapi.shopgoodwill.com`).

| Project | What it does |
|---|---|
| **GoodwillBidder** | Snipes one auction. It waits until the last few seconds, checks the current price, then places your bid. |
| **GoodwillPicker** | Recommends auctions. It learns your taste from your favorited items and ranks auctions closing soon by how well they match. It never bids. |

Both target **.NET 10**.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A shopgoodwill account (see [Credentials](#credentials))

## Build

```sh
dotnet build GoodwillBidder.sln
```

---

## GoodwillBidder (auction sniper)

```sh
dotnet run --project GoodwillBidder -- <itemId> <maxBid> [--dryrun] [--simclose=<seconds>]
```

If you leave out `itemId` and `maxBid`, the program asks for them.

| Option | Description |
|---|---|
| `--dryrun` | Runs everything (fetch, login, timing, bid decision) against the live auction but skips the actual `PlaceBid` request. |
| `--simclose=<seconds>` | Ignores the real close time and acts as if the auction closes `<seconds>` from now. Use it to watch the whole sequence quickly on any item, even a closed one. Best used with `--dryrun`. |

Example:

```sh
dotnet run --project GoodwillBidder -- 268355491 34 --dryrun --simclose=30
```

### How it works

1. **Fetch the item and sync clocks.** It loads the item details and works out the offset between this PC's clock and the server's clock from the response's `serverTime`, using the midpoint of the request's round trip. All timing uses the server's clock, so it doesn't matter if your PC clock is off or in a different time zone.
2. **Sleep** until about 10 seconds before close. If the auction has already closed, it exits.
3. **Log in** and time how long the login took.
4. **Phase A: price check.** A few seconds before the bid goes out, it fetches the current price again and refines the clock offset. This slow call happens early so it doesn't delay the bid.
5. **Phase B: place the bid.** It fires the bid about 4 seconds before close, plus extra time if the login was slow (clamped to 3.5–12 s). The server accepts a bid by the time it *receives* it, and `PlaceBid` responses can take about 3 s under end-of-auction load, so the request goes out early.
6. **Bid amount.** It bids `maxBid`. If the current price is already higher than `maxBid`, it bids `ceil(maxBid * 1.05)` instead.
7. **Outbid retry.** If the response says you've already been outbid (and the 5% bump wasn't used yet), it rebids once at `ceil(maxBid * 1.05)`.

The program waits for Enter before exiting so you can read the output.

---

## GoodwillPicker (recommendations)

```sh
dotnet run --project GoodwillPicker -- [options]
```

| Option | Default | Description |
|---|---|---|
| `--category=<id>` | — | Search only this category ID and skip the menu. |
| `--auto` | off | Skip the menu and search your top 5 favorite categories. |
| `--favtype=<open\|close\|all>` | `all` | Which favorites to learn from. |
| `--days=<n>` | `0` | Include auctions closing within N extra days (`0` = today only). |
| `--top=<n>` | `20` | How many recommendations to print. |
| `--pages=<n>` | `8` | Search pages to scan per category (40 items per page). |
| `--sort=<n>` / `--sortdesc` | `1` | Sort column for the search API, and whether to reverse it. |
| `--maxfav=<n>` | `80` | Maximum number of favorites to fetch details for. |
| `--html` | off | Also write an HTML page of the picks and open it in a browser. |
| `--out=<path>` | temp file | Where to write the HTML page. |
| `--no-open` | off | With `--html`, write the file but don't open the browser. |

Example:

```sh
dotnet run --project GoodwillPicker -- --auto --days=1 --top=30 --html
```

### How it works

1. Logs in and reads your favorited item IDs.
2. Fetches details for each favorite and builds a **taste profile** of keywords and categories.
3. Chooses which categories to search: the one from `--category`, your top favorite categories with `--auto`, or the ones you pick from an interactive menu.
4. Scans current listings and keeps the ones closing by the end of the window (in server time). Items you've already favorited are skipped.
5. Ranks the remaining items against your profile using TF-IDF scoring and prints the top picks. With `--html`, it also writes them to an HTML report.

Source layout:
- `Program.cs`: command-line handling and the overall steps
- `Sgw.cs`: API client, profile building and ranking
- `Report.cs`: HTML output
- `Models.cs`: data types

---

## Credentials

Both tools log in through `POST /api/SignIn/Login` with a **pre-encrypted login payload**. The shopgoodwill web app encrypts the username and password in the browser, and the payload is copied from one of those login requests. The payload is read from **`credentials.json`** in the solution folder. Git ignores this file.

Setup:

1. Copy `credentials.example.json` to `credentials.json`.
2. Log in at shopgoodwill.com with your browser's dev tools open and find the `SignIn/Login` request.
3. Copy the `userName` and `password` values from its JSON body into `credentials.json` exactly as they appear. They are already encrypted and URL-encoded.

The file is looked for in the current directory, then in the app's directory and each folder above it, so it works with `dotnet run` and from Visual Studio. The shared loader is in `Shared/LoginConfig.cs`, which both projects link in. GoodwillBidder loads the file at startup, so a missing or invalid file causes an immediate error instead of a failure at bid time.

> ⚠️ This payload works like a password. Never commit `credentials.json`.

## Disclaimer

This project uses an undocumented API that can change without notice. Use it at your own risk.

## Other folders

- `UnicodeGrid/`: an unrelated scratch library that reads a published Google Doc table of `(x, char, y)` rows and prints it as a character grid. It is not part of the solution.
