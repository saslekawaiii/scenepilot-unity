using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WorldWeaver.Editor.Assistant
{
    [Serializable] public sealed class MaterialDescriptor
    {
        public string Id, Name, Source, License = "CC0-1.0", SourceUrl, Tags;
        public bool Imported;
    }
    [Serializable] public sealed class MaterialSearchResult
    {
        public List<MaterialDescriptor> Materials = new List<MaterialDescriptor>();
        public string Warning = "", Credit = "Powered by Poly Haven";
    }
    public interface IMaterialHttpTransport
    {
        Task<byte[]> GetAsync(Uri uri, int maxBytes, CancellationToken token);
    }
    /// <summary>No caller-supplied URLs, redirects, credentials, executable content or archives.</summary>
    public sealed class MaterialHttpTransport : IMaterialHttpTransport
    {
        public async Task<byte[]> GetAsync(Uri uri, int maxBytes, CancellationToken token)
        {
            PolyHavenMaterialClient.ValidateEndpoint(uri);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, Credentials = null, AutomaticDecompression = DecompressionMethods.None })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(35));
                request.Headers.TryAddWithoutValidation("User-Agent", "WorldWeaver-Unity/1.3 (PolyHaven-Material-Library)");
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) throw new IOException("Poly Haven redirects are not followed.");
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > maxBytes) throw new IOException("Material response exceeds the download budget.");
                    using (var input = await response.Content.ReadAsStreamAsync())
                    using (var output = new MemoryStream())
                    {
                        byte[] buffer = new byte[16384]; int count;
                        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) != 0)
                        {
                            if (output.Length + count > maxBytes) throw new IOException("Material response exceeds the download budget.");
                            output.Write(buffer, 0, count);
                        }
                        return output.ToArray();
                    }
                }
            }
        }
    }
    public sealed class MaterialMapDownload
    {
        public string Role, Extension, Url, Md5, Sha256;
        public byte[] Bytes;
    }
    public sealed class MaterialDownload
    {
        public MaterialDescriptor Descriptor;
        public List<MaterialMapDownload> Maps = new List<MaterialMapDownload>();
    }
    /// <summary>Small public-API client, isolated from Unity imports for protocol testing.</summary>
    public sealed class PolyHavenMaterialClient
    {
        public const int MaxImageBytes = 8 * 1024 * 1024, MaxTotalBytes = 24 * 1024 * 1024;
        private readonly IMaterialHttpTransport http;
        private readonly Dictionary<string, MaterialDescriptor[]> searches = new Dictionary<string, MaterialDescriptor[]>();
        public PolyHavenMaterialClient(IMaterialHttpTransport transport = null) { http = transport ?? new MaterialHttpTransport(); }
        public static string ValidateSlug(string slug)
        {
            if (string.IsNullOrEmpty(slug) || !Regex.IsMatch(slug, "^[a-z0-9][a-z0-9_]{0,79}$", RegexOptions.CultureInvariant))
                throw new ArgumentException("Material ID must contain only a known provider prefix and a lower-case asset slug.");
            return slug;
        }
        public static void ValidateEndpoint(Uri uri)
        {
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Only the official HTTPS material provider is supported.");
            bool api = uri.Host.Equals("api.polyhaven.com", StringComparison.OrdinalIgnoreCase) &&
                (uri.AbsolutePath == "/search" || Regex.IsMatch(uri.AbsolutePath, "^/(info|files)/[a-z0-9][a-z0-9_]{0,79}$"));
            bool image = uri.Host.Equals("dl.polyhaven.org", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith("/file/ph-assets/Textures/", StringComparison.Ordinal) && string.IsNullOrEmpty(uri.Query) &&
                (uri.AbsolutePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
            if (!api && !image) throw new ArgumentException("URL is outside the supported Poly Haven metadata/texture endpoints.");
        }
        private async Task<JObject> Json(string path, CancellationToken token)
        {
            var bytes = await http.GetAsync(new Uri("https://api.polyhaven.com" + path), 1024 * 1024, token);
            return JObject.Parse(Encoding.UTF8.GetString(bytes));
        }
        private static MaterialDescriptor Descriptor(string slug, JObject info)
        {
            if ((int?)info["type"] != 1) throw new InvalidDataException("The provider asset is not a texture.");
            return new MaterialDescriptor { Id = "polyhaven:" + slug, Name = SafeText((string)info["name"] ?? slug, 120), Source = "Poly Haven",
                SourceUrl = "https://polyhaven.com/a/" + slug,
                Tags = SafeText(string.Join(", ", (info["tags"] as JArray ?? new JArray()).Values<string>().Take(20)), 350) };
        }
        private static string SafeText(string value, int max) => new string((value ?? "").Where(c => !char.IsControl(c)).Take(max).ToArray());
        public async Task<MaterialDescriptor[]> SearchAsync(string query, int limit, CancellationToken token = default)
        {
            using(var requestBudget=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
            requestBudget.CancelAfter(TimeSpan.FromSeconds(90));token=requestBudget.Token;
            query = (query ?? "").Trim().ToLowerInvariant();
            if (query.Length == 0 || query.Length > 100) throw new ArgumentException("Texture search needs 1–100 characters.");
            if (limit < 1 || limit > 8) throw new ArgumentOutOfRangeException(nameof(limit));
            string key = query + "|" + limit;
            if (searches.TryGetValue(key, out var cached)) return cached;
            var result = await Json("/search?q=" + Uri.EscapeDataString(query) + "&type=textures&limit=" + limit, token);
            var list = new List<MaterialDescriptor>();
            foreach (var item in (result["results"] as JArray ?? new JArray()).Take(limit))
            {
                token.ThrowIfCancellationRequested();
                string slug = ValidateSlug((string)item["slug"]);
                list.Add(Descriptor(slug, await Json("/info/" + slug, token)));
            }
            if (searches.Count >= 32) searches.Clear();
            return searches[key] = list.ToArray();
            }
        }
        private static JObject SelectMap(JObject files, string[] aliases)
        {
            foreach (string alias in aliases)
            {
                var property = files.Properties().FirstOrDefault(p => p.Name.Equals(alias, StringComparison.OrdinalIgnoreCase));
                var formats = property?.Value?["1k"] as JObject;
                if (formats?["jpg"] is JObject jpg) return (JObject)jpg.DeepClone();
                if (formats?["png"] is JObject png) return (JObject)png.DeepClone();
            }
            return null;
        }
        public async Task<MaterialDownload> DownloadAsync(string slug, CancellationToken token = default)
        {
            using(var requestBudget=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
            requestBudget.CancelAfter(TimeSpan.FromSeconds(90));token=requestBudget.Token;
            ValidateSlug(slug);
            var descriptor = Descriptor(slug, await Json("/info/" + slug, token));
            var files = await Json("/files/" + slug, token);
            var result = new MaterialDownload { Descriptor = descriptor }; int total = 0;
            string[][] roles = { new[] { "albedo", "Diffuse", "diff", "color", "basecolor" }, new[] { "normal", "nor_gl" },
                new[] { "roughness", "Rough", "roughness" }, new[] { "occlusion", "AO" }, new[] { "metallic", "Metal", "metallic" } };
            foreach (var role in roles)
            {
                var file = SelectMap(files, role.Skip(1).ToArray());
                if (file == null) { if (role[0] == "albedo") throw new InvalidDataException("No supported 1k albedo map; choose another material or a built-in fallback."); continue; }
                string url = (string)file["url"], md5 = ((string)file["md5"] ?? "").ToLowerInvariant();
                var uri = new Uri(url); ValidateEndpoint(uri);
                long declared = (long?)file["size"] ?? -1;
                if (declared < 1 || declared > MaxImageBytes || !Regex.IsMatch(md5, "^[0-9a-f]{32}$")) throw new InvalidDataException("Provider texture lacks a bounded size and MD5 checksum.");
                if (total + declared > MaxTotalBytes) throw new InvalidDataException("The material exceeds the 24 MiB budget.");
                var bytes = await http.GetAsync(uri, MaxImageBytes, token);
                if (bytes.LongLength != declared || Hash(bytes, false) != md5) throw new InvalidDataException("Provider texture size/checksum mismatch.");
                ValidateImageHeader(bytes); total += bytes.Length;
                result.Maps.Add(new MaterialMapDownload { Role = role[0], Extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant(), Url = url, Md5 = md5, Sha256 = Hash(bytes, true), Bytes = bytes });
            }
            return result;
            }
        }
        public static string Hash(byte[] bytes, bool sha256 = true)
        {
            using (HashAlgorithm algorithm = sha256 ? (HashAlgorithm)SHA256.Create() : MD5.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public static void ValidateImageHeader(byte[] bytes)
        {
            int width = 0, height = 0;
            if (bytes == null || bytes.Length < 24 || bytes.Length > MaxImageBytes) throw new InvalidDataException("Invalid texture payload size.");
            if (bytes.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) && Encoding.ASCII.GetString(bytes,12,4)=="IHDR")
            {
                width = ReadBig(bytes,16,4); height = ReadBig(bytes,20,4);
            }
            else if (bytes[0] == 255 && bytes[1] == 216)
            {
                int i = 2;
                while (i + 4 < bytes.Length)
                {
                    if (bytes[i++] != 255) throw new InvalidDataException("Invalid JPEG marker.");
                    while (i < bytes.Length && bytes[i] == 255) i++;
                    if (i >= bytes.Length) break;
                    int marker = bytes[i++];
                    if (marker == 217 || marker == 218) break;
                    if (marker == 1 || marker >= 208 && marker <= 215) continue;
                    int length = ReadBig(bytes,i,2);
                    if (length < 2 || i + length > bytes.Length) throw new InvalidDataException("Invalid JPEG section.");
                    if (new[] {192,193,194,195,197,198,199,201,202,203,205,206,207}.Contains(marker))
                    {
                        if (length < 8) throw new InvalidDataException("Invalid JPEG frame.");
                        height = ReadBig(bytes,i+3,2); width = ReadBig(bytes,i+5,2); break;
                    }
                    i += length;
                }
            }
            if (width < 16 || height < 16 || width > 2048 || height > 2048 || (long)width * height > 2048L * 2048)
                throw new InvalidDataException("Only bounded PNG/JPEG texture images up to 2048×2048 are accepted.");
        }
        private static int ReadBig(byte[] bytes,int offset,int count)
        {
            if (offset < 0 || offset + count > bytes.Length) throw new InvalidDataException("Truncated image header.");
            long value=0; for(int i=0;i<count;i++) value=(value<<8)|bytes[offset+i];
            if(value>int.MaxValue) throw new InvalidDataException("Oversized image dimensions."); return (int)value;
        }
    }
}
