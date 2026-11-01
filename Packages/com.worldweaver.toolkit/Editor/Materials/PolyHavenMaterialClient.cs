using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
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
    public sealed class MaterialTransferProgress
    {
        public string File;public int Bytes,TotalBytes,Requests;public bool RangeFallback;
    }
    /// <summary>No caller-supplied URLs, redirects, credentials, executable content or archives.</summary>
    public sealed class MaterialHttpTransport : IMaterialHttpTransport
    {
        public const int RangeChunkBytes=16384,MaxRangeRequests=600,RangeParallelism=3;
        private readonly Func<HttpMessageHandler> handlerFactory;
        private readonly IProgress<MaterialTransferProgress> progress;
        private sealed class Payload {public byte[] Bytes;public long Total;public string ETag;public bool Whole;}
        public MaterialHttpTransport(Func<HttpMessageHandler> handlerFactory=null,IProgress<MaterialTransferProgress> progress=null)
        {
            this.handlerFactory=handlerFactory??(()=>new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,Credentials=null,AutomaticDecompression=DecompressionMethods.None});
            this.progress=progress;
        }
        public async Task<byte[]> GetAsync(Uri uri, int maxBytes, CancellationToken token)
        {
            PolyHavenMaterialClient.ValidateEndpoint(uri);
            if(uri.Host.Equals("dl.polyhaven.org",StringComparison.OrdinalIgnoreCase))
            {
                try {return (await Request(uri,maxBytes,token,12,null,null,null,null).ConfigureAwait(false)).Bytes;}
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch(Exception error)when(error is OperationCanceledException||error is HttpRequestException||error is IOException&&!(error is InvalidDataException))
                {return await Ranges(uri,maxBytes,token).ConfigureAwait(false);}
            }
            return (await Request(uri,maxBytes,token,35,null,null,null,null).ConfigureAwait(false)).Bytes;
        }
        private async Task<Payload> Request(Uri uri,int maxBytes,CancellationToken token,int seconds,long? from,long? to,long? total,string etag)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var handler = handlerFactory())
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
                request.Headers.TryAddWithoutValidation("User-Agent", "WorldWeaver-Unity/1.3 (PolyHaven-Material-Library)");
                if(from.HasValue)
                {
                    request.Headers.Range=new RangeHeaderValue(from,to);
                    // A fresh, closed connection per fragment avoids stalled reused Mono TLS streams.
                    request.Headers.ConnectionClose=true;
                    if(!string.IsNullOrEmpty(etag))request.Headers.IfRange=new RangeConditionHeaderValue(new EntityTagHeaderValue(etag));
                }
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) throw new InvalidDataException("Poly Haven redirects are not followed.");
                    response.EnsureSuccessStatusCode();
                    long expected=-1,length=response.Content.Headers.ContentLength??-1;bool whole=!from.HasValue;
                    string actualEtag=response.Headers.ETag?.IsWeak==false?response.Headers.ETag.Tag:null;
                    if(!from.HasValue&&response.StatusCode!=HttpStatusCode.OK)
                    {
                        var unsolicited=response.Content.Headers.ContentRange;
                        if(response.StatusCode!=HttpStatusCode.PartialContent)throw new InvalidDataException("Unexpected material response status.");
                        if(unsolicited==null||unsolicited.Unit!="bytes"||!unsolicited.HasLength||!unsolicited.HasRange||unsolicited.From!=0||unsolicited.To!=unsolicited.Length-1)
                            throw new IOException("Unsolicited partial texture response; retry using explicit bounded ranges.");
                        expected=unsolicited.Length.Value;
                        if(expected<1||expected>maxBytes||length>=0&&length!=expected)throw new InvalidDataException("Whole 206 response has an invalid length or exceeds the download budget.");
                    }
                    if(from.HasValue)
                    {
                        if(response.StatusCode==HttpStatusCode.OK&&from.Value==0)
                        {
                            // A provider may ignore Range; accept one bounded complete object, never append it.
                            whole=true;expected=length;
                        }
                        else
                        {
                            var range=response.Content.Headers.ContentRange;
                            if(response.StatusCode!=HttpStatusCode.PartialContent||range==null||range.Unit!="bytes"||!range.HasLength||!range.HasRange)
                                throw new InvalidDataException("Texture server did not return a valid 206 Content-Range.");
                            long size=range.Length.Value,end=Math.Min(to.Value,size-1);
                            if(size<1||size>maxBytes||range.From!=from||range.To!=end||total.HasValue&&size!=total.Value||!string.IsNullOrEmpty(etag)&&actualEtag!=etag)
                                throw new InvalidDataException("Texture Content-Range/total/ETag changed or is outside the download budget.");
                            expected=end-from.Value+1;
                            if(expected<1||length>=0&&length!=expected)throw new InvalidDataException("Texture fragment length does not match Content-Range.");
                            total=size;
                        }
                    }
                    if(length>maxBytes||whole&&from.HasValue&&length==0)throw new InvalidDataException("Material response exceeds or omits the download budget.");
                    int responseLimit=whole?maxBytes:(int)expected;
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        byte[] buffer = new byte[16384]; int count;
                        while(true)
                        {
                            using(var stall=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
                            {
                                stall.CancelAfter(TimeSpan.FromSeconds(5));
                                count=await input.ReadAsync(buffer,0,buffer.Length,stall.Token).ConfigureAwait(false);
                            }
                            if(count==0)break;
                            if (output.Length + count > responseLimit) throw new InvalidDataException("Material response exceeds the download budget.");
                            output.Write(buffer, 0, count);
                        }
                        if(length>=0&&output.Length!=length||expected>=0&&output.Length!=expected)throw new IOException("Texture response was truncated.");
                        return new Payload{Bytes=output.ToArray(),Total=whole?output.Length:total.Value,ETag=actualEtag,Whole=whole};
                    }
                }
            }
        }
        private async Task<byte[]> Ranges(Uri uri,int maxBytes,CancellationToken token)
        {
            int requests=0,completed=0;
            using(var operation=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Func<long,long,long?,string,Task<Payload>> fetch=async(start,end,total,etag)=>
                {
                    for(int attempt=0;;attempt++)
                    {
                        if(Interlocked.Increment(ref requests)>MaxRangeRequests)throw new InvalidDataException("The bounded texture fragment request budget was exhausted.");
                        try{return await Request(uri,maxBytes,operation.Token,12,start,end,total,etag).ConfigureAwait(false);}
                        catch(OperationCanceledException)when(operation.IsCancellationRequested){throw;}
                        catch(Exception error)when(attempt==0&&(error is OperationCanceledException||error is HttpRequestException||error is IOException&&!(error is InvalidDataException))){}
                    }
                };
                var first=await fetch(0,RangeChunkBytes-1,null,null).ConfigureAwait(false);
                if(first.Whole)return first.Bytes;
                var result=new byte[(int)first.Total];Buffer.BlockCopy(first.Bytes,0,result,0,first.Bytes.Length);completed=first.Bytes.Length;
                progress?.Report(new MaterialTransferProgress{File=Path.GetFileName(uri.AbsolutePath),Bytes=completed,TotalBytes=result.Length,Requests=requests,RangeFallback=true});
                int next=RangeChunkBytes;Exception failure=null;
                Func<Task> worker=async()=>
                {
                    try
                    {
                        while(true)
                        {
                            int start=Interlocked.Add(ref next,RangeChunkBytes)-RangeChunkBytes;if(start>=result.Length)return;
                            int end=Math.Min(start+RangeChunkBytes,result.Length)-1;
                            var part=await fetch(start,end,result.Length,first.ETag).ConfigureAwait(false);
                            if(part.Whole||part.Bytes.Length!=end-start+1)throw new InvalidDataException("Texture server ignored a nonzero Range; no whole response was appended.");
                            Buffer.BlockCopy(part.Bytes,0,result,start,part.Bytes.Length);int bytes=Interlocked.Add(ref completed,part.Bytes.Length);
                            progress?.Report(new MaterialTransferProgress{File=Path.GetFileName(uri.AbsolutePath),Bytes=bytes,TotalBytes=result.Length,Requests=requests,RangeFallback=true});
                        }
                    }
                    catch(Exception error){Interlocked.CompareExchange(ref failure,error,null);operation.Cancel();throw;}
                };
                try{await Task.WhenAll(Enumerable.Range(0,RangeParallelism).Select(_=>worker())).ConfigureAwait(false);}
                catch{if(failure!=null)ExceptionDispatchInfo.Capture(failure).Throw();throw;}
                return result;
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
        public const int MaxSearchQueryCharacters = 100, MaxEffectiveSearchQueryCharacters = 100;
        private readonly IMaterialHttpTransport http;
        private readonly Dictionary<string, MaterialDescriptor[]> searches = new Dictionary<string, MaterialDescriptor[]>();
        public PolyHavenMaterialClient(IMaterialHttpTransport transport = null) { http = transport ?? new MaterialHttpTransport(); }
        private sealed class QueryTerm
        {
            public readonly Regex Pattern; public readonly string English;
            public QueryTerm(string pattern, string english)
            { Pattern = new Regex("^(?:" + pattern + ")$", RegexOptions.CultureInvariant); English = english; }
        }
        // Literal material vocabulary only. Full-word inflections avoid broad stems such as
        // дерев*: деревня/деревенский must never silently become a request for wood.
        private const string Adjective = "(?:ый|ий|ой|ая|яя|ое|ее|ые|ие|ого|его|ей|ому|ему|ым|им|ую|юю|ых|их|ыми|ими)";
        private static readonly QueryTerm[] QueryTerms = {
            new QueryTerm(@"дерев(?:о|а|у|ом|е|ья|ьев|ьям|ьями|ьях)|деревянн"+Adjective+@"|древесин(?:а|ы|у|е|ой|ою)", "wood"),
            new QueryTerm(@"доск(?:а|и|у|е|ой|ою|ам|ами|ах)|досок", "planks"),
            new QueryTerm(@"бревн(?:о|а|у|ом|е|ами|ах)", "logs"),
            new QueryTerm(@"(?:выветренн|обветренн)"+Adjective, "weathered"),
            new QueryTerm(@"состаренн"+Adjective, "aged"),
            new QueryTerm(@"(?:изношенн|потерт)"+Adjective, "worn"),
            new QueryTerm(@"стар"+Adjective, "old"),
            new QueryTerm(@"кам(?:ень|ня|ню|нем|не|ни|ней|ням|нями|нях)|каменн"+Adjective, "stone"),
            new QueryTerm(@"скал(?:а|ы|у|е|ой|ою|ам|ами|ах)", "rock"),
            new QueryTerm(@"кирпич(?:и|а|у|ом|е|ей|ам|ами|ах)?|кирпичн"+Adjective, "brick"),
            new QueryTerm(@"трав(?:а|ы|у|е|ой|ою|ам|ами|ах)|травян"+Adjective+@"|травянн"+Adjective, "grass"),
            new QueryTerm(@"луг(?:а|у|ом|е|ам|ами|ах)?|лугов"+Adjective, "meadow"),
            new QueryTerm(@"штукатурк(?:а|и|у|е|ой|ою|ам|ами|ах)|штукатурн"+Adjective, "plaster"),
            new QueryTerm(@"известняк(?:а|у|ом|е|и|ов|ам|ами|ах)?", "limestone"),
            new QueryTerm(@"известков"+Adjective, "lime"),
            new QueryTerm(@"крыш(?:а|и|у|е|ей|ам|ами|ах)|кровл(?:я|и|ю|е|ей|ям|ями|ях)", "roof"),
            new QueryTerm(@"черепиц(?:а|ы|у|е|ей)|черепичн"+Adjective, "roof tiles"),
            new QueryTerm(@"плитк(?:а|и|у|е|ой|ою|ам|ами|ах)", "tiles"),
            new QueryTerm(@"металл(?:ы|а|у|ом|е|ов|ам|ами|ах)?|металлическ"+Adjective, "metal"),
            new QueryTerm(@"стал(?:ь|и|ью|ей|ям|ями|ях)|стальн"+Adjective, "steel"),
            new QueryTerm(@"желез(?:о|а|у|ом|е)|железн"+Adjective, "iron"),
            new QueryTerm(@"ржавчин(?:а|ы|у|е|ой|ою)", "rust"),
            new QueryTerm(@"ржав"+Adjective, "rusty"),
            new QueryTerm(@"пес(?:ок|ка|ку|ком|ке|ки|ков|кам|ками|ках)|песчан"+Adjective, "sand"),
            new QueryTerm(@"грунт(?:ы|а|у|ом|е|ов|ам|ами|ах)?|почв(?:а|ы|у|е|ой|ою)", "soil"),
            new QueryTerm(@"земл(?:я|и|ю|е|ей|ям|ями|ях)", "ground"),
            new QueryTerm(@"гряз(?:ь|и|ью|ей)", "dirt"),
            new QueryTerm(@"дорог(?:а|и|у|е|ам|ами|ах)", "road"),
            new QueryTerm(@"снег(?:а|у|ом|е|и|ов|ам|ами|ах)?|снежн"+Adjective, "snow"),
            new QueryTerm(@"лед|льд(?:а|у|ом|е|ы|ов|ам|ами|ах)|ледян"+Adjective, "ice"),
            new QueryTerm(@"кор(?:а|ы|у|е|ой|ою)", "bark"),
            new QueryTerm(@"ствол(?:ы|а|у|ом|е|ов|ам|ами|ах)?", "trunk"),
            new QueryTerm(@"ткан(?:ь|и|ью|ей|ям|ями|ях)|тканев"+Adjective, "fabric"),
            new QueryTerm(@"сукн(?:о|а|у|ом|е)", "cloth"),
            new QueryTerm(@"хлоп(?:ок|ка|ку|ком|ке)|хлопков"+Adjective, "cotton"),
            new QueryTerm(@"шерст(?:ь|и|ью|ей)|шерстян"+Adjective, "wool"),
            new QueryTerm(@"кож(?:а|и|у|е|ей)|кожан"+Adjective, "leather"),
            new QueryTerm(@"вод(?:а|ы|у|е|ой|ою|ам|ами|ах)|водн"+Adjective, "water"),
            new QueryTerm(@"озер(?:о|а|у|ом|е|ами|ах)", "lake"),
            new QueryTerm(@"ряб(?:ь|и|ью)", "ripples"),
            new QueryTerm(@"брусчатк(?:а|и|у|е|ой|ою)", "cobblestone"),
            new QueryTerm(@"мостов(?:ая|ой|ую|ые|ых|ыми)", "pavement"),
            new QueryTerm(@"мощен"+Adjective, "paving"),
            new QueryTerm(@"шероховат"+Adjective+@"|груб"+Adjective, "rough"),
            new QueryTerm(@"гладк"+Adjective, "smooth"),
            new QueryTerm(@"полированн"+Adjective, "polished"),
            new QueryTerm(@"матов"+Adjective, "matte"),
            new QueryTerm(@"темн"+Adjective, "dark"),
            new QueryTerm(@"светл"+Adjective, "light"),
            new QueryTerm(@"красн"+Adjective, "red"),
            new QueryTerm(@"сер"+Adjective, "grey"),
            new QueryTerm(@"бел"+Adjective, "white"),
            new QueryTerm(@"черн"+Adjective, "black"),
            new QueryTerm(@"зелен"+Adjective, "green"),
            new QueryTerm(@"коричнев"+Adjective, "brown")
        };
        private static readonly Regex RussianQueryWord = new Regex(@"(?<![\p{L}\p{N}_])[а-яё]+(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant);
        internal static string NormaliseSearchQuery(string query)
        {
            if (query == null || query.Length == 0 || query.Length > MaxSearchQueryCharacters || query.Any(c => char.IsControl(c) && !char.IsWhiteSpace(c)))
                throw new ArgumentException("Texture search needs 1–100 input characters without control codes.", nameof(query));
            string input = Regex.Replace(query.Trim().ToLowerInvariant(), @"\s+", " ");
            if (input.Length == 0) throw new ArgumentException("Texture search needs a nonempty material query.", nameof(query));
            string effective = RussianQueryWord.Replace(input, match => {
                string word = match.Value.Replace('ё', 'е');
                return QueryTerms.FirstOrDefault(term => term.Pattern.IsMatch(word))?.English ?? match.Value;
            });
            if (effective.Length > MaxEffectiveSearchQueryCharacters)
                throw new ArgumentException("The English material query exceeds 100 characters; use fewer material terms.", nameof(query));
            return effective;
        }
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
            token.ThrowIfCancellationRequested();
            query = NormaliseSearchQuery(query);
            if (limit < 1 || limit > 8) throw new ArgumentOutOfRangeException(nameof(limit));
            using(var requestBudget=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
            requestBudget.CancelAfter(TimeSpan.FromSeconds(90));token=requestBudget.Token;
            string key = query + "|" + limit;
            token.ThrowIfCancellationRequested();
            if (searches.TryGetValue(key, out var cached)) return cached;
            var result = await Json("/search?q=" + Uri.EscapeDataString(query) + "&type=textures&limit=" + limit, token);
            token.ThrowIfCancellationRequested();
            var list = new List<MaterialDescriptor>();
            foreach (var item in (result["results"] as JArray ?? new JArray()).Take(limit))
            {
                token.ThrowIfCancellationRequested();
                string slug = ValidateSlug((string)item["slug"]);
                list.Add(Descriptor(slug, await Json("/info/" + slug, token)));
            }
            token.ThrowIfCancellationRequested();
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
