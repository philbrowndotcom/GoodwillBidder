namespace GoodwillBidder.Models
{
    public class BidRequest
    {
        public int itemId { get; set; }
        public int quantity { get; set; }
        public int sellerId { get; set; }
        public string bidAmount { get; set; }
    }

    public class BidComplete
    {
        public int bidLogId { get; set; }
        public int itemId { get; set; }
        public double itemPrice { get; set; }
        public double bidAmount { get; set; }
        public DateTime bidTime { get; set; }
        public int quantity { get; set; }
        public object bidIPAddress { get; set; }
        public string adCode { get; set; }
        public string serverIP { get; set; }
        public bool retracted { get; set; }
        public string bidderName { get; set; }
        public string highBidderName { get; set; }
        public bool isBuyerHighBidder { get; set; }
        public bool isLoggedInBuyer { get; set; }
    }

    public class BidHistory
    {
        public bool auctionClosed { get; set; }
        public string authenticatedBuyerLogin { get; set; }
        public double itemCurrentPrice { get; set; }
        public double itemReservePrice { get; set; }
        public bool isItemResearve { get; set; }
        public bool isReserveMet { get; set; }
        public bool showQuantity { get; set; }
        public bool isHighBidderLogIn { get; set; }
        public List<BidSummary> bidSummary { get; set; }
        public List<BidComplete> bidComplete { get; set; }
    }

    public class BidSummary
    {
        public int bidHistoryId { get; set; }
        public int itemId { get; set; }
        public int quantity { get; set; }
        public int quantityWon { get; set; }
        public string bidderName { get; set; }
        public double amount { get; set; }
        public bool buyNow { get; set; }
        public DateTime time { get; set; }
        public bool isLoggedInBuyer { get; set; }
    }

    public class BuyerShippingAddress
    {
        public string address { get; set; }
        public object addressType { get; set; }
        public int buyerId { get; set; }
        public string city { get; set; }
        public string country { get; set; }
        public string countryCode { get; set; }
        public object name { get; set; }
        public int shippingAddressId { get; set; }
        public string state { get; set; }
        public int validated { get; set; }
        public string zip { get; set; }
    }

    public class CategoryBreadCrumb
    {
        public int level { get; set; }
        public string categoryId { get; set; }
        public object group { get; set; }
        public string name { get; set; }
        public object path { get; set; }
        public string shortName { get; set; }
        public object contentBlock { get; set; }
    }

    public class Root
    {
        public object buyerCountry { get; set; }
        public string buyerCountryCode { get; set; }
        public object buyerState { get; set; }
        public object buyerStreet { get; set; }
        public object buyerZip { get; set; }
        public string categoryParentList { get; set; }
        public object defaultShippingResults { get; set; }
        public string description { get; set; }
        public string imageServer { get; set; }
        public string imageUrlString { get; set; }
        public string keyWords { get; set; }
        public string noInternationalShippingMessage { get; set; }
        public string pickupCity { get; set; }
        public string pickupCountry { get; set; }
        public string pickupHours { get; set; }
        public string pickupPolicy { get; set; }
        public string shippingPolicy { get; set; }
        public string pickupState { get; set; }
        public string pickupStreet { get; set; }
        public string pickupZip { get; set; }
        public string sellerCompanyName { get; set; }
        public string sellerCountryCode { get; set; }
        public string sellerItemProfile { get; set; }
        public string sellerLandingPageName { get; set; }
        public string sellerShipperName { get; set; }
        public string thumbnailUrlString { get; set; }
        public string title { get; set; }
        public int discount { get; set; }
        public int categoryId { get; set; }
        public int itemId { get; set; }
        public int numberOfBids { get; set; }
        public int maxQuantity { get; set; }
        public int quantity { get; set; }
        public int sellerId { get; set; }
        public int sellerShipper { get; set; }
        public int shipping { get; set; }
        public string remainingTime { get; set; }
        public double bidIncrement { get; set; }
        public double buyNowPrice { get; set; }
        public double startingPrice { get; set; }
        public double currentPrice { get; set; }
        public double defaultShippingPrice { get; set; }
        public double discountedBuyNowPrice { get; set; }
        public double handlingPrice { get; set; }
        public double minimumBid { get; set; }
        public double reserve { get; set; }
        public bool isReserveMet { get; set; }
        public bool isItemResearve { get; set; }
        public double shippingPrice { get; set; }
        public double displayWeight { get; set; }
        public double weight { get; set; }
        public bool allowShippingCalculation { get; set; }
        public bool insurance { get; set; }
        public bool inWatchlist { get; set; }
        public bool isAuction { get; set; }
        public bool isStock { get; set; }
        public bool noCombineShipping { get; set; }
        public bool payPalPayment { get; set; }
        public bool pickupOnly { get; set; }
        public bool storePickupOnly { get; set; }
        public bool visaMastercardPayment { get; set; }
        public DateTime endTime { get; set; }
        public DateTime serverTime { get; set; }
        public DateTime startTime { get; set; }
        public BidHistory bidHistory { get; set; }
        public object seller { get; set; }
        public object images { get; set; }
        public List<CategoryBreadCrumb> categoryBreadCrumbs { get; set; }
        public List<object> sellerItems { get; set; }
        public object suggestedItems { get; set; }
        public List<BuyerShippingAddress> buyerShippingAddresses { get; set; }
        public string missionImpactBannerLogo { get; set; }
        public string missionImpactBannerData1 { get; set; }
        public string missionImpactBannerData2 { get; set; }
        public string missionImpactBannerData3 { get; set; }
        public string missionImpactBannerData4 { get; set; }
        public string missionImpactBannerLabel1 { get; set; }
        public string missionImpactBannerLabel2 { get; set; }
        public string missionImpactBannerLabel3 { get; set; }
        public string missionImpactBannerLabel4 { get; set; }
        public bool isItemEndTimeExpire { get; set; }
        public bool sellerAllowPickup { get; set; }
        public string sellerCustomerService { get; set; }
        public string itemPartNumber { get; set; }
        public object category { get; set; }
        public object subcategory { get; set; }
        public bool isBuyNowUsed { get; set; }
        public int listingType { get; set; }
    }

    public class Buyer
    {
        public string buyerId { get; set; }
        public string login { get; set; }
        public string firstName { get; set; }
        public string middleInitial { get; set; }
        public string lastName { get; set; }
        public string email { get; set; }
        public DateTime lastLoginDate { get; set; }
        public string country { get; set; }
    }

    public class RefreshToken
    {
        public string token { get; set; }
        public DateTime expires { get; set; }
        public DateTime created { get; set; }
        public string createdByIp { get; set; }
    }

    public class TokenRoot
    {
        public bool status { get; set; }
        public string message { get; set; }
        public string accessToken { get; set; }
        public RefreshToken refreshToken { get; set; }
        public Buyer buyer { get; set; }
        public bool isUnconfirmedBuyer { get; set; }
    }
}