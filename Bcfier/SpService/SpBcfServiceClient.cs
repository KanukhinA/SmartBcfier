using System;
using System.Collections.Generic;
using System.Globalization;
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
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
          ["grant_type"] = "password",
          ["username"] = login ?? string.Empty,
          ["password"] = password ?? string.Empty
        });
        using HttpResponseMessage oauth = await _http.PostAsync("/foundation/1.0/oauth2/token", form).ConfigureAwait(false);
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
      using HttpResponseMessage response = await _http.GetAsync("/bcf/3.0/projects").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить список BCF-проектов.");
      return await ReadJson<List<BcfApiProject>>(response, Snake).ConfigureAwait(false) ?? new List<BcfApiProject>();
    }

    /// <summary>Опциональный список SP Project для админ-фильтра (proprietary).</summary>
    public async Task<IReadOnlyList<ProjectItem>> GetProjectsAsync()
    {
      using HttpResponseMessage response = await _http.GetAsync("/api/projects").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить список проектов.");
      return await ReadJson<List<ProjectItem>>(response, Camel).ConfigureAwait(false) ?? new List<ProjectItem>();
    }

    public async Task<IReadOnlyList<ModelItem>> GetModelsAsync(Guid projectId)
    {
      using HttpResponseMessage response = await _http.GetAsync($"/api/projects/{projectId:D}/models").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить список моделей.");
      return await ReadJson<List<ModelItem>>(response, Camel).ConfigureAwait(false) ?? new List<ModelItem>();
    }

    public async Task<IReadOnlyList<BcfApiTopic>> GetTopicsAsync(string projectId)
    {
      using HttpResponseMessage response = await _http.GetAsync($"/bcf/3.0/projects/{projectId}/topics").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить topics.");
      return await ReadJson<List<BcfApiTopic>>(response, Snake).ConfigureAwait(false) ?? new List<BcfApiTopic>();
    }

    public async Task<IReadOnlyList<BcfApiComment>> GetCommentsAsync(string projectId, string topicGuid)
    {
      using HttpResponseMessage response = await _http
        .GetAsync($"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/comments").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить comments.");
      return await ReadJson<List<BcfApiComment>>(response, Snake).ConfigureAwait(false) ?? new List<BcfApiComment>();
    }

    public async Task<IReadOnlyList<BcfApiViewpoint>> GetViewpointsAsync(string projectId, string topicGuid)
    {
      using HttpResponseMessage response = await _http
        .GetAsync($"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/viewpoints").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить viewpoints.");
      return await ReadJson<List<BcfApiViewpoint>>(response, Snake).ConfigureAwait(false) ?? new List<BcfApiViewpoint>();
    }

    public async Task<byte[]> DownloadViewpointSnapshotAsync(string projectId, string topicGuid, string viewpointGuid)
    {
      using HttpResponseMessage response = await _http
        .GetAsync($"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/viewpoints/{viewpointGuid}/snapshot")
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
      string json = JsonConvert.SerializeObject(body, Snake);
      using var content = new StringContent(json, Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await _http
        .PutAsync($"/bcf/3.0/projects/{projectId}/topics/{topicGuid}", content).ConfigureAwait(false);
      if ((int)response.StatusCode == 409)
        throw new SpBcfConflictException("Конфликт topic.", null);
      EnsureSuccess(response, "Не удалось обновить topic.");
      return await ReadJson<BcfApiTopic>(response, Snake).ConfigureAwait(false);
    }

    public async Task DeleteTopicAsync(string projectId, string topicGuid)
    {
      using HttpResponseMessage response = await _http
        .DeleteAsync($"/bcf/3.0/projects/{projectId}/topics/{topicGuid}").ConfigureAwait(false);
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
      string json = JsonConvert.SerializeObject(body, Snake);
      using var content = new StringContent(json, Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await _http
        .PutAsync($"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/comments/{commentGuid}", content)
        .ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось обновить comment.");
      return await ReadJson<BcfApiComment>(response, Snake).ConfigureAwait(false);
    }

    public async Task DeleteCommentAsync(string projectId, string topicGuid, string commentGuid)
    {
      using HttpResponseMessage response = await _http
        .DeleteAsync($"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/comments/{commentGuid}")
        .ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось удалить comment.");
    }

    public async Task<BcfApiViewpoint> CreateViewpointAsync(string projectId, string topicGuid, JObject body)
    {
      using var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await _http
        .PostAsync($"/bcf/3.0/projects/{projectId}/topics/{topicGuid}/viewpoints", content)
        .ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось создать viewpoint.");
      return await ReadJson<BcfApiViewpoint>(response, Snake).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BcfApiTopicEvent>> GetTopicEventsAsync(string projectId, DateTimeOffset? since)
    {
      string url = $"/bcf/3.0/projects/{projectId}/topics/events";
      if (since != null)
        url += "?since=" + Uri.EscapeDataString(since.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
      using HttpResponseMessage response = await _http.GetAsync(url).ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось получить topic events.");
      return await ReadJson<List<BcfApiTopicEvent>>(response, Snake).ConfigureAwait(false) ?? new List<BcfApiTopicEvent>();
    }

    /// <summary>Fallback: proprietary export zip (для полного VisInfo XML при необходимости).</summary>
    public async Task<byte[]> ExportBcfAsync(Guid bcfFileId)
    {
      using HttpResponseMessage response = await _http.GetAsync($"/api/bcf-files/{bcfFileId:D}/export").ConfigureAwait(false);
      EnsureSuccess(response, "Не удалось выгрузить BCF.");
      return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();

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

    private async Task<HttpResponseMessage> PostJson(string url, object body, JsonSerializerSettings settings)
    {
      string json = JsonConvert.SerializeObject(body, settings);
      return await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
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
      public string BcfVersion { get; set; }
      public DateTimeOffset UpdatedAt { get; set; }
      public string DisplayName => Name ?? Id.ToString("D");
      public override string ToString() => DisplayName;
    }
  }

  public sealed class SpBcfConflictException : Exception
  {
    public SpBcfConflictException(string message, object serverState) : base(message) { }
  }
}
