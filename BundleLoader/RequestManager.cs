using System;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Reflection;
using System.Text;
using ComponentAce.Compression.Libs.zlib;
using MelonLoader;
using Newtonsoft.Json;

namespace BundleLoader
{
    [Obfuscation(Exclude = true)]
    internal static class RequestManager
    {
        private static string BackendUrl => GClass333.Config.BackendUrl;
        public static WebClient Client = new WebClient { CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore) };
        private static readonly string BundleDir = Path.Combine(AppContext.BaseDirectory, "Bundles/Local");
        private static readonly string BundleCacheDir = Path.Combine(AppContext.BaseDirectory, "Bundles/Cache");

        static RequestManager()
        {
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        public static Bundle[] GetServerBundles()
        {
            var json = GetJson("/singleplayer/bundles");

            var bundles = JsonConvert.DeserializeObject<Bundle[]>(json);

            return bundles;
        }
        public static string GetLocalBundlePath(this Bundle bundle)
        {
            try
            {
                var local = false;
                var backend = new Uri(BackendUrl);
                if (IPAddress.TryParse(backend.Host, out var ip))
                    if (ip.MapToIPv4().ToString().StartsWith("127"))
                        local = true;


                if (local && File.Exists(bundle.path))
                    return bundle.path;

                // Check local bundles folder
                var possibleLocalPath = Path.Combine(BundleDir, bundle.key);
                if (File.Exists(possibleLocalPath))
                    return possibleLocalPath;

                // Check local cache
                var cachePath = Path.Combine(BundleCacheDir, backend.Host, bundle.key);
                if (File.Exists(cachePath))
                    return cachePath;

                // Download bundle and put it in the cache folder
                var url = BackendUrl + "/files/bundle/" + bundle.key;
                var dirPath = Path.GetDirectoryName(cachePath);
                if (!Directory.Exists(dirPath))
                    Directory.CreateDirectory(dirPath);

                MelonLogger.Log("Downloading bundle from " + url);
                Client.DownloadFile(url, cachePath);

                return cachePath;
            }
            catch (Exception e)
            {
                MelonLogger.Log(e.Data);
                return null;
            }
        }
        public static string GetJson(string url, bool compress = true)
        {
            using var stream = Send(url, "GET", null, compress);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return SimpleZlib.Decompress(ms.ToArray(), null);
        }
        private static Stream Send(string url, string method = "GET", string data = null, bool compress = true)
        {
            // disable SSL encryption
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            // set session headers
            var request = WebRequest.Create(new Uri(BackendUrl + url));

            request.Headers.Add("Accept-Encoding", "deflate");
            request.Method = method;

            if (method != "GET" && !string.IsNullOrWhiteSpace(data))
            {
                // set request body
                var bytes = (compress) ? SimpleZlib.CompressToBytes(data, zlibConst.Z_BEST_COMPRESSION) : Encoding.UTF8.GetBytes(data);

                request.ContentType = "application/json";
                request.ContentLength = bytes.Length;

                if (compress)
                    request.Headers.Add("Content-Encoding", "deflate");

                using var stream = request.GetRequestStream();
                stream.Write(bytes, 0, bytes.Length);
            }

            // get response stream
            try
            {
                var response = request.GetResponse();
                return response.GetResponseStream();
            }
            catch (Exception e)
            {
                MelonLogger.LogError(e.ToString());
            }

            return null;
        }

        public class Bundle
        {
            public string key { get; set; }
            public string path { get; set; }
            public string[] dependencyKeys { get; set; }
        }
    }
}
