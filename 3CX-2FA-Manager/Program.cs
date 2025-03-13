using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

class Program
{
    private static HttpClient _httpClient = new HttpClient();
    private static string _fqdn3Cx;
    private static string _clientId;
    private static string _token;
    private static string _enable2FAValue;

    private static async Task<string> InitConnection()
    {
        var parameters = new Dictionary<string, string>
        {
            { "client_id", _clientId },
            { "client_secret", _token },
            { "grant_type", "client_credentials" }
        };

        var encodedContent = new FormUrlEncodedContent(parameters);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_fqdn3Cx}/connect/token")
        {
            Content = encodedContent
        };

        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        Console.WriteLine($"Sending authentication request to {_fqdn3Cx}");

        var response = await _httpClient.SendAsync(requestMessage);
        var responseBody = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Authentication Response: {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Authentication failed: {response.StatusCode} - {responseBody}");
        }

        using JsonDocument doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("access_token", out var tokenElement))
        {
            return tokenElement.GetString();
        }
        throw new Exception("Access token not found");
    }

    static StringContent GetContent(int id, bool require2FA)
    {
        var data = new { Id = id, Require2FA = require2FA };
        string json = JsonSerializer.Serialize(data);

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return content;
    }

    static async Task Main()
    {
        Console.WriteLine($"Starting application at {DateTime.Now}");

        var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
        var settings3Cx = config.GetSection("3CXSettings");

        _fqdn3Cx = settings3Cx["FQDN_3CX"];
        _clientId = settings3Cx["ApiClientID_3CX"];
        _token = settings3Cx["ApiToken_3CX"];
        _enable2FAValue = settings3Cx["Enable2FA3CX"];
        Console.WriteLine($"Reading configuration:\n[FQDN_3CX] : [{_fqdn3Cx}]\n[ApiClientID_3CX] : [{_clientId}]\n[Enable2FA3CX] : [{_enable2FAValue}]");

        bool require2FA = false;
        if (_enable2FAValue.Equals("true", StringComparison.InvariantCultureIgnoreCase))
        {
            require2FA = true;
        }
        else if (_enable2FAValue.Equals("false", StringComparison.InvariantCultureIgnoreCase))
        {
            require2FA = false;
        }
        else
        {
            Console.WriteLine("Invalid value for Enable2FA3CX");
            Environment.Exit(1);
        }
        Console.WriteLine($"Change value Enable2FA3CX to: {_enable2FAValue}");

        string usersToChangeStr = settings3Cx["UsersToChange"];
        List<int> usersToChange = usersToChangeStr.Split(',').Select(int.Parse).ToList();

        try
        {
            string accessToken = await InitConnection();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            Console.WriteLine($"Starting updates");
            foreach (int userId in usersToChange)
            {
                try
                {
                    var content = GetContent(userId, require2FA);
                    string requestUrl = $"{_fqdn3Cx}/xapi/v1/Users({userId})";

                    var request = new HttpRequestMessage(HttpMethod.Patch, requestUrl)
                    {
                        Content = content
                    };

                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    HttpResponseMessage response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response for User ID {userId}: {response.StatusCode}, {responseBody}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Request failed for User ID {userId}: {ex.Message}");
                }
            }
        }
        catch (Exception authEx)
        {
            Console.WriteLine($"Authentication failed: {authEx.Message}");
        }
    }
}
