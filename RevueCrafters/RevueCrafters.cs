
using NUnit.Framework;
using RestSharp;
using RestSharp.Authenticators;
using System;
using System.Net;
using System.Text.Json;
using RevueCrafters.Models;

namespace RevueCraftersTests
{
    [TestFixture]
    public class RevueCraftersApiTests
    {
        private RestClient client;
        private static string lastRevueId;

        private const string FallbackEmail = "user@examplee.com";
        private const string FallbackPassword = "string12";

        [OneTimeSetUp]
        public void Setup()
        {
            client = new RestClient("https://d2925tksfvgq8c.cloudfront.net");

            var email = Environment.GetEnvironmentVariable("REVUE_EMAIL") ?? FallbackEmail;
            var password = Environment.GetEnvironmentVariable("REVUE_PASSWORD") ?? FallbackPassword;
            Assert.False(string.IsNullOrWhiteSpace(email), "Email is empty.");
            Assert.False(string.IsNullOrWhiteSpace(password), "Password is empty.");

            string token = TryLogin(email, password);
            if (token == null)
            {
                var create = new RestRequest("/api/User/Create", Method.Post)
                    .AddJsonBody(new
                    {
                        userName = email.Split('@')[0],
                        email = email,
                        password = password,
                        rePassword = password,
                        acceptedAgreement = true
                    });
                client.Execute(create);
                token = TryLogin(email, password);
            }
            Assert.IsNotNull(token, "Login failed.");

            var opts = new RestClientOptions("https://d2925tksfvgq8c.cloudfront.net")
            {
                Authenticator = new JwtAuthenticator(token)
            };
            client = new RestClient(opts);
        }

        private string TryLogin(string email, string password)
        {
            var login = new RestRequest("/api/User/Authentication", Method.Post)
                .AddJsonBody(new { email = email, password = password });
            var r = client.Execute(login);
            if (r.StatusCode != HttpStatusCode.OK || string.IsNullOrWhiteSpace(r.Content)) return null;
            try { return JsonDocument.Parse(r.Content).RootElement.GetProperty("accessToken").GetString(); }
            catch { return null; }
        }

        [OneTimeTearDown]
        public void Cleanup() => client?.Dispose();

        [Test, Order(1)]
        public void CreateRevue()
        {
            var req = new RestRequest("/api/Revue/Create", Method.Post);
            req.AddJsonBody(new RevueDTO
            {
                Title = "My First Test Revue",
                Url = "",
                Description = "Testing create revue"
            });
            var resp = client.Execute(req);
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, resp.Content);

            var dto = JsonSerializer.Deserialize<ApiResponseDTO>(resp.Content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.AreEqual("Successfully created!", dto.Msg, resp.Content);
        }

        [Test, Order(2)]
        public void GetAllRevues()
        {
            var req = new RestRequest("/api/Revue/All", Method.Get);
            var resp = client.Execute(req);

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, resp.Content);
            Assert.IsNotNull(resp.Content, "Empty body.");

            var doc = JsonDocument.Parse(resp.Content);
            var root = doc.RootElement;

            JsonElement arr = root;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                    arr = dataProp;
                else if (root.TryGetProperty("revues", out var revuesProp) && revuesProp.ValueKind == JsonValueKind.Array)
                    arr = revuesProp;
            }

            Assert.AreEqual(JsonValueKind.Array, arr.ValueKind, "Expected an array of revues.");
            Assert.Greater(arr.GetArrayLength(), 0, "No revues returned.");

            var last = arr[arr.GetArrayLength() - 1];

            string id = null;
            if (last.ValueKind == JsonValueKind.Object)
            {
                if (last.TryGetProperty("revueId", out var v1) && v1.ValueKind == JsonValueKind.String) id = v1.GetString();
                else if (last.TryGetProperty("id", out var v2) && v2.ValueKind == JsonValueKind.String) id = v2.GetString();
                else if (last.TryGetProperty("_id", out var v3) && v3.ValueKind == JsonValueKind.String) id = v3.GetString();
            }

            Assert.False(string.IsNullOrWhiteSpace(id), "Could not read revue id from the last item.");
            lastRevueId = id;
        }

        [Test, Order(3)]
        public void EditLastRevue()
        {
            Assert.False(string.IsNullOrWhiteSpace(lastRevueId), "lastRevueId is empty. Did GetAllRevues fail?");
            var req = new RestRequest("/api/Revue/Edit", Method.Put)
                .AddQueryParameter("revueId", lastRevueId)
                .AddJsonBody(new RevueDTO { Title = "Edited Revue", Url = "", Description = "Edited description" });

            var resp = client.Execute(req);
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, resp.Content);

            var dto = JsonSerializer.Deserialize<ApiResponseDTO>(resp.Content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.AreEqual("Edited successfully", dto.Msg, resp.Content);
        }

        [Test, Order(4)]
        public void DeleteLastRevue()
        {
            Assert.False(string.IsNullOrWhiteSpace(lastRevueId), "lastRevueId is empty. Did GetAllRevues fail?");
            var req = new RestRequest("/api/Revue/Delete", Method.Delete)
                .AddQueryParameter("revueId", lastRevueId);

            var resp = client.Execute(req);
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, resp.Content);

            var dto = JsonSerializer.Deserialize<ApiResponseDTO>(resp.Content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.AreEqual("The revue is deleted!", dto.Msg, resp.Content);
        }

        [Test, Order(5)]
        public void CreateRevueWithoutRequiredFields()
        {
            var req = new RestRequest("/api/Revue/Create", Method.Post)
                .AddJsonBody(new RevueDTO { Title = "", Url = "", Description = "" });

            var resp = client.Execute(req);
            Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode, resp.Content);
        }

        [Test, Order(6)]
        public void EditNonExistingRevue()
        {
            var req = new RestRequest("/api/Revue/Edit", Method.Put)
                .AddQueryParameter("revueId", "non-existing-id")
                .AddJsonBody(new RevueDTO { Title = "Fake", Url = "", Description = "Fake revue" });

            var resp = client.Execute(req);
            Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode, resp.Content);

            var dto = JsonSerializer.Deserialize<ApiResponseDTO>(resp.Content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.AreEqual("There is no such revue!", dto.Msg, resp.Content);
        }

        [Test, Order(7)]
        public void DeleteNonExistingRevue()
        {
            var req = new RestRequest("/api/Revue/Delete", Method.Delete)
                .AddQueryParameter("revueId", "non-existing-id");

            var resp = client.Execute(req);
            Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode, resp.Content);

            var dto = JsonSerializer.Deserialize<ApiResponseDTO>(resp.Content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.AreEqual("There is no such revue!", dto.Msg, resp.Content);
        }
    }
}
