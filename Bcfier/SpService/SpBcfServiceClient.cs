using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Bcfier.SpService
{
  /// <summary>HTTP-клиент buildingSMART BCF-API 3.0 через Foundation OAuth.</summary>
  public sealed class SpBcfServiceClient : IDisposable
  {
    private static readonly JsonSerializerSettings Snake = new JsonSerializerSettings
    {
      ContractResolver = new DefaultContractResolver
      {
        NamingStrategy = new SnakeCaseNamingStrategy()
      },
      NullValueHandling = NullValueHandling.Ignore
    };

    private static readonly JsonSerializerSettings Camel = new JsonSerializerSettings
    {
      ContractResolver = new CamelCasePropertyNamesContractResolver(),
      NullValueHandling = NullValueHandling.Ignore
    };

    private readonly HttpClient _http;

    /// <summary>Максимум попыток на один HTTP-запрос к SP/BCF-серверу (включая первую).</summary>
    private const int MaxNetworkAttempts = 3;

    public SpBcfServiceClient(string baseUrl)
    {
      string normalized = SpBcfServiceSettings.NormalizeBaseUrl(baseUrl);
      if (string.IsNullOrWhiteSpace(normalized))
        throw new ArgumentException("Не задан адрес сервера.");

      _http = new HttpClient
      {
        BaseAddress = new Uri(normalized.TrimEnd('/') + "/", UriKind.Absolute),
        Timeout = TimeSpan.FromSeconds(120)
      };
      AttachWindowsUserHeader(_http);
    }

    /// <summary>Вход только через Foundation OAuth2 password grant.</summary>
    public async Task<string> LoginAsync(string login, string password)
    {
      try
      {
        using HttpResponseMessage oauth = await SendWithRetryAsync(() =>
        {
          var form = new FormUrlEncodedContent(new Dictionary<string, string>
          {
            ["grant_type"] = "password",
            ["username"] = login ?? string.Empty,
            ["password"] = password ?? string.Empty
          });
          return _http.PostAsync("/foundation/1.0/oauth2/token", form);
        }).ConfigureAwait(false);
        if (oauth.IsSuccessStatusCode)
        {
          string json = await oauth.Content.ReadAsStringAsync().ConfigureAwait(false);
          JObject token = JObject.Parse(json);
          string access = token.Value<string>("access_token");
          if (!string.IsNullOrWhiteSpace(access))
          {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
            return login;
          }
        }

        throw new InvalidOperationException("Foundation OAuth2 не вернул access_token.");
      }
      catch (InvalidOperationException)
      {
        throw;
      }
      catch (Exception ex)
      {
        throw new InvalidOperationException("Не удалось выполнить вход через Foundation OAuth2: " + ex.Message, ex);
      }
    }

    public async Task<IReadOnlyList<BcfApiProject>> GetBcfApiProjectsAsync()
    {
      using HttpResponseMessage response = await GetAsync("/bcf/3.0/projects").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить список BCF-проектов.");
      return await ReadJson<List<BcfApiProject>>(response, Snake).ConfigureAwait(false) ?? new List<BcfApiProject>();
    }

    /// <summary>Опциональный список SP Project для админ-фильтра (proprietary).</summary>
    public async Task<IReadOnlyList<ProjectItem>> GetProjectsAsync()
    {
      using HttpResponseMessage response = await GetAsync("/api/projects").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить список проектов.");
      return await ReadJson<List<ProjectItem>>(response, Camel).ConfigureAwait(false) ?? new List<ProjectItem>();
    }

    public async Task<IReadOnlyList<ModelItem>> GetModelsAsync(Guid projectId)
    {
      using HttpResponseMessage response = await GetAsync($"/api/projects/{projectId:D}/models").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить список моделей.");
      return await ReadJson<List<ModelItem>>(response, Camel).ConfigureAwait(false) ?? new List<ModelItem>();
    }

    public async Task<IReadOnlyList<ProjectBcfFileItem>> ListProjectBcfFilesAsync(Guid projectId)
    {
      using HttpResponseMessage response = await GetAsync($"/api/projects/{projectId:D}/bcf-files").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить список BCF-отчётов проекта.");
      return await ReadJson<List<ProjectBcfFileItem>>(response, Camel).ConfigureAwait(false)
             ?? new List<ProjectBcfFileItem>();
    }

    public async Task<ProjectBcfFileItem> PublishBcfAsync(
      Guid projectId,
      string modelName,
      string bcfName,
      string revitTitle,
      string pathHash,
      string centralModelGuid,
      string tempBcfPath)
    {
      using HttpResponseMessage response = await SendWithRetryAsync(() =>
        BuildPublishRequest(projectId, modelName, bcfName, revitTitle, pathHash, centralModelGuid, tempBcfPath))
        .ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new InvalidOperationException(
          "Не удалось сохранить BCF в БД. Код: " + (int)response.StatusCode
          + (string.IsNullOrWhiteSpace(err) ? "" : ". " + err));
      }

      return await ReadJson<ProjectBcfFileItem>(response, Camel).ConfigureAwait(false)
             ?? new ProjectBcfFileItem();
    }

    private Task<HttpResponseMessage> BuildPublishRequest(
      Guid projectId,
      string modelName,
      string bcfName,
      string revitTitle,
      string pathHash,
      string centralModelGuid,
      string tempBcfPath)
    {
      var form = new MultipartFormDataContent();
      form.Add(new StringContent(modelName ?? string.Empty), "modelName");
      if (!string.IsNullOrWhiteSpace(bcfName))
        form.Add(new StringContent(bcfName), "bcfName");
      if (!string.IsNullOrWhiteSpace(revitTitle))
        form.Add(new StringContent(revitTitle), "revitTitle");
      if (!string.IsNullOrWhiteSpace(pathHash))
        form.Add(new StringContent(pathHash), "pathHash");
      if (!string.IsNullOrWhiteSpace(centralModelGuid))
        form.Add(new StringContent(centralModelGuid), "centralModelGuid");

      if (!string.IsNullOrWhiteSpace(tempBcfPath) && File.Exists(tempBcfPath))
      {
        var fileStream = File.OpenRead(tempBcfPath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(tempBcfPath));
      }

      return _http.PostAsync($"/api/projects/{projectId:D}/bcf-files/publish", form);
    }

    public async Task<ProjectMyRole> GetMyRoleAsync(Guid projectId)
    {
      using HttpResponseMessage response = await GetAsync($"/api/projects/{projectId:D}/my-role").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить роль в проекте.");
      return await ReadJson<ProjectMyRole>(response, Camel).ConfigureAwait(false) ?? new ProjectMyRole();
    }

    public async Task<GoogleSheetsSettings> GetGoogleSheetsSettingsAsync(Guid projectId)
    {
      using HttpResponseMessage response = await GetAsync($"/api/projects/{projectId:D}/google-sheets").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить настройки Google Sheets.");
      return await ReadJson<GoogleSheetsSettings>(response, Camel).ConfigureAwait(false) ?? new GoogleSheetsSettings();
    }

    public async Task<GoogleSheetsSettings> PutGoogleSheetsSettingsAsync(Guid projectId, string serviceAccountJson, bool? enabled)
    {
      var body = new { serviceAccountJson, enabled };
      using HttpResponseMessage response = await PostJsonAsync(
        $"/api/projects/{projectId:D}/google-sheets", body, Camel, HttpMethod.Put).ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось сохранить настройки Google Sheets.");
      return await ReadJson<GoogleSheetsSettings>(response, Camel).ConfigureAwait(false) ?? new GoogleSheetsSettings();
    }

    public async Task<GoogleSheetsTestResult> TestGoogleSheetsAsync(Guid projectId)
    {
      using HttpResponseMessage response = await SendWithRetryAsync(() =>
          _http.PostAsync(
            $"/api/projects/{projectId:D}/google-sheets/test",
            new StringContent("{}", Encoding.UTF8, "application/json")))
        .ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось проверить Google Sheets.");
      return await ReadJson<GoogleSheetsTestResult>(response, Camel).ConfigureAwait(false) ?? new GoogleSheetsTestResult();
    }

    public async Task<GoogleSheetsExportResult> ExportGoogleSheetsAsync(Guid bcfFileId, GoogleSheetsExportRequest request)
    {
      using HttpResponseMessage response = await PostJsonAsync(
        $"/api/bcf-files/{bcfFileId:D}/google-sheets/export", request, Camel, HttpMethod.Post).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new InvalidOperationException(
          "Не удалось выгрузить в Google Sheets. Код: " + (int)response.StatusCode
          + (string.IsNullOrWhiteSpace(err) ? "" : ". " + err));
      }
      return await ReadJson<GoogleSheetsExportResult>(response, Camel).ConfigureAwait(false)
             ?? new GoogleSheetsExportResult();
    }

    private Task<HttpResponseMessage> PostJsonAsync(
      string url, object body, JsonSerializerSettings settings, HttpMethod method)
    {
      return SendWithRetryAsync(() =>
      {
        string json = JsonConvert.SerializeObject(body, settings);
        var request = new HttpRequestMessage(method, url)
        {
          Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return _http.SendAsync(request);
      });
    }

    public async Task<IReadOnlyList<BcfApiTopic>> GetTopicsAsync(string projectId)
    {
      using HttpResponseMessage response = await GetAsync($"/bcf/3.0/projects/{projectId}/topics").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить topics.");
      return await ReadJson<List<BcfApiTopic>>(response, Snake).ConfigureAwait(false) ?? new List<BcfApiTopic>();
    }

    public async Task<IReadOnlyList<BcfApiComment>> GetCommentsAsync(string projectId, string topicGuid)
    {
      using HttpResponseMessage response = await GetAsync(
        $"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/comments").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить comments.");
      return await ReadJson<List<BcfApiComment>>(response, Snake).ConfigureAwait(false) ?? new List<BcfApiComment>();
    }

    public async Task<IReadOnlyList<BcfApiViewpoint>> GetViewpointsAsync(string projectId, string topicGuid)
    {
      using HttpResponseMessage response = await GetAsync(
        $"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/viewpoints").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить viewpoints.");
      return await ReadJson<List<BcfApiViewpoint>>(response, Snake).ConfigureAwait(false) ?? new List<BcfApiViewpoint>();
    }

    public async Task<byte[]> DownloadViewpointSnapshotAsync(string projectId, string topicGuid, string viewpointGuid)
    {
      using HttpResponseMessage response = await GetAsync(
          $"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/viewpoints/{viewpointGuid}/snapshot")
        .ConfigureAwait(false);
      if (response.StatusCode == HttpStatusCode.NotFound)
        return null;
      EnsureSuccess(response, "Не удалось скачать snapshot.");
      return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    public async Task<BcfApiTopic> CreateTopicAsync(string projectId, object body)
    {
      using HttpResponseMessage response = await PostJson($"/bcf/3.0/projects/{projectId}/topics", body, Snake).ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось создать topic.");
      return await ReadJson<BcfApiTopic>(response, Snake).ConfigureAwait(false);
    }

    public async Task<BcfApiTopic> UpdateTopicAsync(string projectId, string topicGuid, object body)
    {
      using HttpResponseMessage response = await PutJsonAsync(
        $"/bcf/3.0/projects/{projectId}/topics/{topicGuid}", body, Snake).ConfigureAwait(false);
      if ((int)response.StatusCode == 409)
        throw new SpBcfConflictException("Конфликт topic.", null);
      EnsureSuccess(response, "Не удалось обновить topic.");
      return await ReadJson<BcfApiTopic>(response, Snake).ConfigureAwait(false);
    }

    public async Task DeleteTopicAsync(string projectId, string topicGuid)
    {
      using HttpResponseMessage response = await DeleteAsync(
        $"/bcf/3.0/projects/{projectId}/topics/{topicGuid}").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось удалить topic.");
    }

    public async Task<BcfApiComment> CreateCommentAsync(string projectId, string topicGuid, object body)
    {
      using HttpResponseMessage response = await PostJson(
        $"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/comments", body, Snake).ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось создать comment.");
      return await ReadJson<BcfApiComment>(response, Snake).ConfigureAwait(false);
    }

    public async Task<BcfApiComment> UpdateCommentAsync(string projectId, string topicGuid, string commentGuid, object body)
    {
      using HttpResponseMessage response = await PutJsonAsync(
        $"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/comments/{commentGuid}", body, Snake)
        .ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось обновить comment.");
      return await ReadJson<BcfApiComment>(response, Snake).ConfigureAwait(false);
    }

    public async Task DeleteCommentAsync(string projectId, string topicGuid, string commentGuid)
    {
      using HttpResponseMessage response = await DeleteAsync(
        $"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/comments/{commentGuid}")
        .ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось удалить comment.");
    }

    public async Task<BcfApiViewpoint> CreateViewpointAsync(string projectId, string topicGuid, JObject body)
    {
      using HttpResponseMessage response = await SendWithRetryAsync(() =>
          _http.PostAsync(
            $"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/viewpoints",
            new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")))
        .ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось создать viewpoint.");
      return await ReadJson<BcfApiViewpoint>(response, Snake).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BcfApiTopicEvent>> GetTopicEventsAsync(string projectId, DateTimeOffset? since)
    {
      string url = $"/bcf/3.0/projects/{projectId}/topics/events";
      if (since != null)
        url += "?since=" + Uri.EscapeDataString(since.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
      using HttpResponseMessage response = await GetAsync(url).ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить topic events.");
      return await ReadJson<List<BcfApiTopicEvent>>(response, Snake).ConfigureAwait(false) ?? new List<BcfApiTopicEvent>();
    }

    /// <summary>Fallback: proprietary export zip (для полного VisInfo XML при необходимости).</summary>
    public async Task<byte[]> ExportBcfAsync(Guid bcfFileId)
    {
      using HttpResponseMessage response = await GetAsync($"/api/bcf-files/{bcfFileId:D}/export").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось выгрузить BCF.");
      return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();

    private Task<HttpResponseMessage> GetAsync(string url) =>
      SendWithRetryAsync(() => _http.GetAsync(url));

    private Task<HttpResponseMessage> DeleteAsync(string url) =>
      SendWithRetryAsync(() => _http.DeleteAsync(url));

    private Task<HttpResponseMessage> PutJsonAsync(string url, object body, JsonSerializerSettings settings)
    {
      return SendWithRetryAsync(() =>
      {
        string json = JsonConvert.SerializeObject(body, settings);
        return _http.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
      });
    }

    /// <summary>
    /// Повторяет запрос при сетевых сбоях и временных ответах сервера (до MaxNetworkAttempts раз).
    /// </summary>
    private static async Task<HttpResponseMessage> SendWithRetryAsync(Func<Task<HttpResponseMessage>> send)
    {
      if (send == null)
        throw new ArgumentNullException(nameof(send));

      HttpResponseMessage lastResponse = null;
      Exception lastError = null;

      for (int attempt = 1; attempt <= MaxNetworkAttempts; attempt++)
      {
        try
        {
          lastResponse?.Dispose();
          lastResponse = await send().ConfigureAwait(false);
          if (!ShouldRetryStatus(lastResponse.StatusCode) || attempt == MaxNetworkAttempts)
            return lastResponse;

          lastResponse.Dispose();
          lastResponse = null;
        }
        catch (Exception ex) when (ShouldRetryException(ex) && attempt < MaxNetworkAttempts)
        {
          lastError = ex;
        }

        await Task.Delay(TimeSpan.FromSeconds(attempt)).ConfigureAwait(false);
      }

      if (lastResponse != null)
        return lastResponse;

      throw lastError ?? new InvalidOperationException(
        "Сетевой запрос к серверу не выполнен после " + MaxNetworkAttempts + " попыток.");
    }

    private static bool ShouldRetryStatus(HttpStatusCode statusCode)
    {
      int code = (int)statusCode;
      return code == 408 || code == 429 || code >= 500;
    }

    private static bool ShouldRetryException(Exception ex)
    {
      if (ex is HttpRequestException || ex is IOException)
        return true;

      // Таймаут HttpClient приходит как TaskCanceledException
      return ex is TaskCanceledException;
    }

    private static void AttachWindowsUserHeader(HttpClient http)
    {
      try
      {
        string name = null;
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
          if (identity != null && !string.IsNullOrWhiteSpace(identity.Name))
            name = identity.Name;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
          string domain = Environment.UserDomainName;
          string user = Environment.UserName;
          if (!string.IsNullOrWhiteSpace(user))
            name = string.IsNullOrWhiteSpace(domain) ? user : domain + "\\" + user;
        }
        if (!string.IsNullOrWhiteSpace(name))
          http.DefaultRequestHeaders.TryAddWithoutValidation("X-SP-Windows-User", name);
      }
      catch { /* ignore */ }
    }

    private Task<HttpResponseMessage> PostJson(string url, object body, JsonSerializerSettings settings)
    {
      return SendWithRetryAsync(() =>
      {
        string json = JsonConvert.SerializeObject(body, settings);
        return _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
      });
    }

    private static async Task<T> ReadJson<T>(HttpResponseMessage response, JsonSerializerSettings settings)
    {
      string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
      if (string.IsNullOrWhiteSpace(json))
        return default;
      return JsonConvert.DeserializeObject<T>(json, settings);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string message)
    {
      if (response.IsSuccessStatusCode)
        return;
      throw new InvalidOperationException(message + " Код: " + (int)response.StatusCode + ".");
    }

    public sealed class LoginResponse
    {
      public Guid UserId { get; set; }
      public string Token { get; set; }
      public string DisplayName { get; set; }
    }

    public sealed class ProjectItem
    {
      public Guid Id { get; set; }
      public string Name { get; set; }
      public override string ToString() => Name;
    }

    public sealed class ModelItem
    {
      public Guid Id { get; set; }
      public Guid ProjectId { get; set; }
      public string Name { get; set; }
      public override string ToString() => Name;
    }

    public sealed class BcfApiProject
    {
      [JsonProperty("project_id")]
      public string ProjectId { get; set; }
      public string Name { get; set; }
      public override string ToString() => Name ?? ProjectId;

      public Guid? ParsedId => Guid.TryParse(ProjectId, out var id) ? id : (Guid?)null;
    }

    public sealed class BcfApiTopic
    {
      public string Guid { get; set; }
      [JsonProperty("server_assigned_id")]
      public string ServerAssignedId { get; set; }
      public string Title { get; set; }
      [JsonProperty("topic_type")]
      public string TopicType { get; set; }
      [JsonProperty("topic_status")]
      public string TopicStatus { get; set; }
      public string Priority { get; set; }
      public string Stage { get; set; }
      public int? Index { get; set; }
      public List<string> Labels { get; set; }
      [JsonProperty("reference_links")]
      public List<string> ReferenceLinks { get; set; }
      [JsonProperty("assigned_to")]
      public string AssignedTo { get; set; }
      [JsonProperty("due_date")]
      public DateTimeOffset? DueDate { get; set; }
      public string Description { get; set; }
      [JsonProperty("creation_author")]
      public string CreationAuthor { get; set; }
      [JsonProperty("creation_date")]
      public DateTimeOffset CreationDate { get; set; }
      [JsonProperty("modified_author")]
      public string ModifiedAuthor { get; set; }
      [JsonProperty("modified_date")]
      public DateTimeOffset? ModifiedDate { get; set; }
    }

    public sealed class BcfApiComment
    {
      public string Guid { get; set; }
      public DateTimeOffset Date { get; set; }
      public string Author { get; set; }
      public string Comment { get; set; }
      [JsonProperty("viewpoint_guid")]
      public string ViewpointGuid { get; set; }
      [JsonProperty("modified_author")]
      public string ModifiedAuthor { get; set; }
      [JsonProperty("modified_date")]
      public DateTimeOffset? ModifiedDate { get; set; }
    }

    public sealed class BcfApiViewpoint
    {
      public string Guid { get; set; }
      public int? Index { get; set; }
      public JObject Snapshot { get; set; }
      [JsonProperty("perspective_camera")]
      public JObject PerspectiveCamera { get; set; }
      [JsonProperty("orthogonal_camera")]
      public JObject OrthogonalCamera { get; set; }
      public JObject Components { get; set; }
    }

    public sealed class BcfApiTopicEvent
    {
      [JsonProperty("topic_guid")]
      public string TopicGuid { get; set; }
      public DateTimeOffset Date { get; set; }
      public string Author { get; set; }
      public List<BcfApiEventAction> Actions { get; set; }
    }

    public sealed class BcfApiEventAction
    {
      public string Type { get; set; }
      public string Value { get; set; }
    }

    // backward-compat aliases used by Settings / picker
    public sealed class BcfFileItem
    {
      public Guid Id { get; set; }
      public string Name { get; set; }
      public string ModelName { get; set; }
      public bool MatchesCurrentModel { get; set; }
      public string BcfVersion { get; set; }
      public DateTimeOffset UpdatedAt { get; set; }
      public string DisplayName
      {
        get
        {
          string report = Name ?? Id.ToString("D");
          string model = string.IsNullOrWhiteSpace(ModelName) ? "?" : ModelName;
          if (MatchesCurrentModel)
            return "● " + report + "  [" + model + "]";
          return report + "  [" + model + "]";
        }
      }
      public override string ToString() => DisplayName;
    }

    public sealed class ProjectBcfFileItem
    {
      public Guid Id { get; set; }
      public Guid ModelId { get; set; }
      public string ModelName { get; set; }
      public string Name { get; set; }
      public string BcfProjectId { get; set; }
      public string BcfVersion { get; set; }
      public long Revision { get; set; }
      public DateTimeOffset UpdatedAt { get; set; }
    }

    public sealed class ProjectMyRole
    {
      public string Role { get; set; }
      public bool IsSystemAdmin { get; set; }
      public bool CanModerate { get; set; }
    }

    public sealed class GoogleSheetsSettings
    {
      public bool Configured { get; set; }
      public string ClientEmail { get; set; }
      public bool Enabled { get; set; }
    }

    public sealed class GoogleSheetsTestResult
    {
      public bool Ok { get; set; }
      public string ClientEmail { get; set; }
      public string Error { get; set; }
    }

    public sealed class GoogleSheetsExportRequest
    {
      public string SpreadsheetId { get; set; }
      public string SheetName { get; set; }
      public List<string> Headers { get; set; }
      public List<string> ColumnKinds { get; set; }
      public List<List<string>> Rows { get; set; }
    }

    public sealed class GoogleSheetsExportResult
    {
      public Guid BcfFileId { get; set; }
      public string SpreadsheetId { get; set; }
      public string SheetName { get; set; }
      public string SpreadsheetUrl { get; set; }
    }
  }

  public sealed class SpBcfConflictException : Exception
  {
    public SpBcfConflictException(string message, object serverState) : base(message) { }
  }
}
