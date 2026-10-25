namespace GoodwillPicker.Models
{
    // --- Login (POST /api/SignIn/Login) response. Same shape the bidder uses. ---
    public class TokenRoot
    {
        public bool status { get; set; }
        public string? message { get; set; }
        public string accessToken { get; set; } = "";
        public RefreshToken? refreshToken { get; set; }
        public Buyer? buyer { get; set; }
        public bool isUnconfirmedBuyer { get; set; }
    }

    public class RefreshToken
    {
        public string? token { get; set; }
        public DateTime expires { get; set; }
        public DateTime created { get; set; }
        public string? createdByIp { get; set; }
    }

    public class Buyer
    {
        public string? buyerId { get; set; }
        public string? login { get; set; }
        public string? firstName { get; set; }
        public string? lastName { get; set; }
        public string? email { get; set; }
        public DateTime lastLoginDate { get; set; }
    }

    // --- Internal working types (not deserialized directly) ---

    /// <summary>Normalized details for a single auction item, from either the
    /// item-detail endpoint (favorites) or a search-result row.</summary>
    public record ItemInfo(
        int ItemId,
        string Title,
        string Keywords,
        int CategoryId,
        DateTime? EndTime,
        double CurrentPrice,
        int NumberOfBids,
        string CategoryName = "",
        string ImageUrl = "");

    /// <summary>The "taste profile" built from the user's favorited items:
    /// how often each keyword and each category appears across favorites.</summary>
    public record FavProfile(
        Dictionary<string, double> Keywords,
        Dictionary<int, double> Categories,
        Dictionary<int, string> CategoryNames,
        HashSet<int> FavoriteItemIds);

    /// <summary>A current auction scored against the favorites profile.</summary>
    public record Candidate(ItemInfo Item, double Score, List<string> MatchedTerms);
}
