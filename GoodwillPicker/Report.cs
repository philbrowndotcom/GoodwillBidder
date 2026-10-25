using System.Net;
using System.Text;
using GoodwillPicker.Models;

namespace GoodwillPicker
{
    /// <summary>Renders the ranked picks as a single self-contained HTML page.</summary>
    public static class Report
    {
        public static string Build(
            IReadOnlyList<Candidate> ranked,
            FavProfile profile,
            string subtitle,
            DateTime generatedLocal,
            TimeSpan serverOffset)
        {
            string CatName(int id) =>
                profile.CategoryNames.TryGetValue(id, out var n) && n.Length > 0 ? n : $"category {id}";

            var sb = new StringBuilder();
            sb.Append("""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Goodwill Picks</title>
<style>
  :root{
    --bg:#0f1115; --card:#1a1d24; --card2:#20242d; --line:#2a2f3a;
    --text:#e8eaed; --muted:#9aa3b2; --accent:#4ea1ff; --good:#37d67a; --warn:#ffb454;
  }
  *{box-sizing:border-box}
  body{margin:0;background:var(--bg);color:var(--text);
    font:15px/1.5 system-ui,-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif}
  header{padding:28px 24px 8px;max-width:1200px;margin:0 auto}
  h1{margin:0 0 4px;font-size:24px;letter-spacing:.2px}
  .sub{color:var(--muted);font-size:13px}
  .grid{max-width:1200px;margin:0 auto;padding:16px 24px 48px;display:grid;gap:16px;
    grid-template-columns:repeat(auto-fill,minmax(300px,1fr))}
  .card{background:var(--card);border:1px solid var(--line);border-radius:14px;overflow:hidden;
    display:flex;flex-direction:column;transition:transform .08s ease,border-color .08s ease}
  .card:hover{transform:translateY(-2px);border-color:var(--accent)}
  .thumb{position:relative;aspect-ratio:4/3;background:var(--card2);display:flex;align-items:center;justify-content:center}
  .thumb img{width:100%;height:100%;object-fit:cover;display:block}
  .thumb .noimg{color:var(--muted);font-size:13px}
  .rank{position:absolute;top:10px;left:10px;background:rgba(0,0,0,.6);color:#fff;
    border-radius:8px;padding:2px 8px;font-size:12px;font-weight:600}
  .score{position:absolute;top:10px;right:10px;background:var(--accent);color:#06121f;
    border-radius:8px;padding:2px 8px;font-size:12px;font-weight:700}
  .body{padding:14px;display:flex;flex-direction:column;gap:10px;flex:1}
  .title{font-weight:600;font-size:14px;line-height:1.35;margin:0}
  .meta{display:flex;flex-wrap:wrap;gap:10px 16px;color:var(--muted);font-size:13px}
  .price{color:var(--good);font-weight:700}
  .soon{color:var(--warn)}
  .cat{color:var(--muted);font-size:12px}
  .chips{display:flex;flex-wrap:wrap;gap:6px}
  .chip{background:var(--card2);border:1px solid var(--line);color:var(--muted);
    border-radius:999px;padding:2px 9px;font-size:11px}
  .actions{margin-top:auto;display:flex;gap:8px;padding-top:4px}
  a.btn,button.btn{flex:1;text-align:center;border-radius:10px;padding:8px 10px;font-size:13px;
    font-weight:600;cursor:pointer;border:1px solid var(--line);text-decoration:none}
  a.btn{background:var(--accent);color:#06121f;border-color:transparent}
  button.btn{background:var(--card2);color:var(--text)}
  button.btn:hover{border-color:var(--accent)}
  .empty{max-width:1200px;margin:40px auto;padding:0 24px;color:var(--muted)}
  footer{max-width:1200px;margin:0 auto;padding:0 24px 40px;color:var(--muted);font-size:12px}
</style>
</head>
<body>
<header>
  <h1>🎯 Goodwill Picks</h1>
""");
            sb.Append("  <div class=\"sub\">").Append(Enc(subtitle))
              .Append(" &middot; generated ").Append(Enc(generatedLocal.ToString("g")))
              .Append("</div>\n</header>\n");

            if (ranked.Count == 0)
            {
                sb.Append("<div class=\"empty\">No matching auctions in the window. Try more pages, a wider day range, or a different category.</div>\n");
            }
            else
            {
                sb.Append("<main class=\"grid\">\n");
                int rank = 1;
                foreach (var r in ranked)
                {
                    var it = r.Item;
                    DateTime? localClose = it.EndTime is DateTime et ? et - serverOffset : null;
                    string closeStr = localClose?.ToString("ddd h:mm tt") ?? "?";
                    bool soon = localClose is DateTime lc && lc - generatedLocal <= TimeSpan.FromHours(1) && lc >= generatedLocal;
                    string url = $"https://shopgoodwill.com/item/{it.ItemId}";
                    string snipe = $"dotnet run --project GoodwillBidder -- {it.ItemId} <maxBid>";

                    sb.Append("  <article class=\"card\">\n");
                    sb.Append("    <div class=\"thumb\">");
                    if (!string.IsNullOrEmpty(it.ImageUrl))
                        sb.Append("<img loading=\"lazy\" src=\"").Append(Enc(it.ImageUrl))
                          .Append("\" onerror=\"this.parentNode.innerHTML='<span class=&quot;noimg&quot;>no image</span>'\">");
                    else
                        sb.Append("<span class=\"noimg\">no image</span>");
                    sb.Append("<span class=\"rank\">#").Append(rank).Append("</span>");
                    sb.Append("<span class=\"score\">").Append(r.Score.ToString("0")).Append("</span>");
                    sb.Append("</div>\n");

                    sb.Append("    <div class=\"body\">\n");
                    sb.Append("      <p class=\"title\">").Append(Enc(it.Title)).Append("</p>\n");
                    sb.Append("      <div class=\"meta\"><span class=\"price\">$")
                      .Append(it.CurrentPrice.ToString("0.00")).Append("</span>")
                      .Append("<span>").Append(it.NumberOfBids).Append(" bid").Append(it.NumberOfBids == 1 ? "" : "s").Append("</span>")
                      .Append("<span class=\"").Append(soon ? "soon" : "").Append("\">closes ").Append(Enc(closeStr)).Append("</span></div>\n");
                    sb.Append("      <div class=\"cat\">").Append(Enc(CatName(it.CategoryId))).Append("</div>\n");

                    if (r.MatchedTerms.Count > 0)
                    {
                        sb.Append("      <div class=\"chips\">");
                        foreach (var m in r.MatchedTerms.Take(8))
                            sb.Append("<span class=\"chip\">").Append(Enc(m)).Append("</span>");
                        sb.Append("</div>\n");
                    }

                    sb.Append("      <div class=\"actions\">");
                    sb.Append("<a class=\"btn\" href=\"").Append(Enc(url)).Append("\" target=\"_blank\" rel=\"noopener\">View</a>");
                    sb.Append("<button class=\"btn\" onclick=\"copy(this,'").Append(EncJs(snipe)).Append("')\">Copy snipe</button>");
                    sb.Append("</div>\n");
                    sb.Append("    </div>\n  </article>\n");
                    rank++;
                }
                sb.Append("</main>\n");
            }

            sb.Append("<footer>Recommend-only. Nothing here places a bid. Use the snipe command with GoodwillBidder to auto-bid at close.</footer>\n");
            sb.Append("""
<script>
function copy(btn, text){
  navigator.clipboard.writeText(text).then(function(){
    var old = btn.textContent; btn.textContent = 'Copied!';
    setTimeout(function(){ btn.textContent = old; }, 1200);
  });
}
</script>
</body>
</html>
""");
            return sb.ToString();
        }

        private static string Enc(string s) => WebUtility.HtmlEncode(s ?? "");
        private static string EncJs(string s) => (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
