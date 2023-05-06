using DiscordBot.Repositories;
using HtmlAgilityPack;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscordBot;

public class KogWebCrawler
{
    public static async Task<KogUserData?> GetUserDataAsync(string playerName)
    {
        // 要先打一次get api，不然kog的post api不會動
        var requestData = new { type = "players", player = playerName };
        var jsonContent = JsonSerializer.Serialize(requestData);
        string responseJson = await MakePostRequestAsync(jsonContent);
        try
        {
            Response? response = JsonSerializer.Deserialize<Response>(responseJson);
            KogUserData data = JsonSerializer.Deserialize<KogUserData>(response!.Data)!;
            return data;
        }
        catch (JsonException ex)
        {
            if (ex.InnerException!.Message.Contains("Cannot get the value of a token type 'StartArray' as a string."))
            {
                await MakeGetRequestAsync($"https://kog.tw/get.php?p=players&p=players&player={playerName}");
                responseJson = await MakePostRequestAsync(jsonContent);
                Response? response = JsonSerializer.Deserialize<Response>(responseJson);
                KogUserData data = JsonSerializer.Deserialize<KogUserData>(response!.Data)!;
                return data;
            }
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
        var doc = new HtmlDocument();
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
            map.Points = int.Parse(ul.SelectSingleNode(".//li[3]").InnerText.Replace("Points", "").Trim().Split()[0]);
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
        [JsonPropertyName("points")]
        public PointType Points { get; set; } = default!;

        [JsonPropertyName("finishedMaps")]
        public FinishedMap[] FinishedMaps { get; set; } = default!;
    }

    public class Response
    {
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("data")]
        public string Data { get; set; } = default!;
    }
}