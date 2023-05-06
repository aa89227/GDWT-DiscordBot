using Amazon.Runtime.Internal.Endpoints.StandardLibrary;
using DiscordBot.Repositories;
using HtmlAgilityPack;
using System.Text.Json;

namespace DiscordBot;

public class KogWebCrawler
{
    public static async Task<KogUserData?> GetUserDataAsync(string playerName)
    {
        try
        {
            // 要先打一次get api，不然kog的post api不會動
            await MakeGetRequestAsync($"https://kog.tw/get.php?p=players&p=players&player={playerName}"); 
            var requestData = new { type = "players", player = playerName };
            var jsonContent = JsonSerializer.Serialize(requestData);
            var responseJson = await MakePostRequestAsync(jsonContent);
            var responseData = JsonSerializer.Deserialize<Response>(responseJson);
            var data = JsonSerializer.Deserialize<KogUserData>(responseData!.data)!;
            return data;
        }
        catch
        {
            throw;
        }
    }

    private static async Task<string> MakePostRequestAsync(string jsonContent)
    {
        var url = "https://kog.tw/api.php";
        using var httpClient = new HttpClient();
        var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(url, content);
        var responseContent = await response.Content.ReadAsStringAsync();
        return responseContent;
    }

    public static async Task<List<KogMap>> GetAllMapAsync()
    {
        var url = "https://kog.tw/get.php?p=maps";
        var responseJson = await MakeGetRequestAsync(url);
        return ParseMapPage(responseJson);
    }

    private static async Task<string> MakeGetRequestAsync(string url)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(url);
        var responseContent = await response.Content.ReadAsStringAsync();
        return responseContent;
    }

    private static List<KogMap> ParseMapPage(string html)
    {
        HtmlDocument doc = new HtmlDocument();
        doc.LoadHtml(html);

        List<KogMap> mapList = new();
        var n = doc.DocumentNode.SelectNodes(".//div[contains(@class, 'card mb-4 box-shadow')]");
        // 選擇所有帶有類名為“card”的元素，並遍歷它們
        foreach (HtmlNode card in doc.DocumentNode.SelectNodes("//div[contains(@class, 'card mb-4 box-shadow')]"))
        {
            // 創建一個新的KogMap對象
            KogMap map = new();

            // 從HTML代碼中解析地圖名稱
            HtmlNode header = card.SelectSingleNode(".//div[contains(@class, 'card-header')]");
            map.MapName = header.SelectSingleNode(".//h4").InnerText.Trim();

            // 從HTML代碼中解析地圖星級、難度、總點數、和製作者名稱
            HtmlNode ul = card.SelectSingleNode(".//ul[contains(@class, 'list-group-flush')]");

            // 從星星的顏色判斷星級
            HtmlNodeCollection stars = ul.SelectNodes(".//i[contains(@class, 'bi-star')]");

            map.Star = stars.Count(s => s.HasClass("bi-star-fill"));

            map.Difficulty = ul.SelectSingleNode(".//li[2]").InnerText.Trim().Split()[0];
            map.Points = int.Parse(ul.SelectSingleNode(".//li[3]").InnerText.Replace("points", "").Trim().Split()[0]);
            map.Author = ul.SelectSingleNode(".//li[4]").InnerText.Trim();

            // 從HTML代碼中解析地圖發布時間
            HtmlNode footer = card.SelectSingleNode(".//div[contains(@class, 'card-footer')]");
            map.ReleasedTime = footer.InnerText.Trim().Replace("Released at ", "");

            // 將解析出的地圖對象添加到地圖列表中
            mapList.Add(map);
        }
        return mapList;
    }

    public class KogUserData
    {
        public PointType points { get; set; } = default!;
        public FinishedMap[] finishedMaps { get; set; } = default!;
    }

    public class Response
    {
        public int status { get; set; }
        public string data { get; set; } = default!;
    }
}