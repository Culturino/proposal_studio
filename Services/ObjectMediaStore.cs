using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

namespace ProposalStudio.Services
{
    /// <summary>
    /// Optional durable copy of wwwroot media in S3. Local disk stays what the API and PDF read.
    /// When Storage:Bucket is empty this does nothing, so localhost is unchanged.
    /// </summary>
    public class ObjectMediaStore : IDisposable
    {
        private readonly IAmazonS3? _s3;
        private readonly string? _bucket;
        private readonly string _prefix;
        private readonly ILogger<ObjectMediaStore> _logger;

        public ObjectMediaStore(IConfiguration config, ILogger<ObjectMediaStore> logger)
        {
            _logger = logger;
            _bucket = config["Storage:Bucket"];
            _prefix = (config["Storage:Prefix"] ?? "").Trim().Trim('/');

            if (string.IsNullOrWhiteSpace(_bucket))
            {
                _s3 = null;
                return;
            }

            var regionName = config["Storage:Region"]
                ?? Environment.GetEnvironmentVariable("AWS_REGION")
                ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
                ?? "eu-central-1";

            _s3 = new AmazonS3Client(RegionEndpoint.GetBySystemName(regionName));
            _logger.LogInformation("Object media store enabled for s3://{Bucket}/{Prefix}", _bucket, _prefix);
        }

        public bool Enabled => _s3 != null && !string.IsNullOrWhiteSpace(_bucket);

        public async Task UploadFileAsync(string relativeKey, string localPath)
        {
            if (!Enabled || !File.Exists(localPath))
                return;

            try
            {
                var request = new PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = Key(relativeKey),
                    FilePath = localPath,
                    ContentType = ContentType(localPath)
                };
                await _s3!.PutObjectAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not upload {Key} to S3", relativeKey);
            }
        }

        public async Task DeleteAsync(string relativeKey)
        {
            if (!Enabled)
                return;

            try
            {
                await _s3!.DeleteObjectAsync(_bucket, Key(relativeKey));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not delete {Key} from S3", relativeKey);
            }
        }

        /// <summary>
        /// Downloads every object under the given wwwroot-relative prefixes if the local file is
        /// missing. Existing local files win so a just-uploaded photo is not overwritten.
        /// </summary>
        public async Task RestoreAsync(string webRoot, params string[] relativePrefixes)
        {
            if (!Enabled)
                return;

            foreach (var relative in relativePrefixes)
            {
                var prefix = Key(relative.Replace('\\', '/').Trim('/'));
                if (!prefix.EndsWith('/'))
                    prefix += '/';

                string? token = null;
                var restored = 0;
                do
                {
                    var list = await _s3!.ListObjectsV2Async(new ListObjectsV2Request
                    {
                        BucketName = _bucket,
                        Prefix = prefix,
                        ContinuationToken = token
                    });

                    foreach (var obj in list.S3Objects ?? [])
                    {
                        if (obj.Key.EndsWith('/'))
                            continue;

                        var relativeKey = StripPrefix(obj.Key);
                        var dest = Path.Combine(webRoot, relativeKey.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(dest))
                            continue;

                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        using var response = await _s3.GetObjectAsync(_bucket, obj.Key);
                        await response.WriteResponseStreamToFileAsync(dest, false, CancellationToken.None);
                        restored++;
                    }

                    token = list.IsTruncated == true ? list.NextContinuationToken : null;
                } while (token != null);

                if (restored > 0)
                    _logger.LogInformation("Restored {Count} files from s3://{Bucket}/{Prefix}", restored, _bucket, prefix);
            }
        }

        public void Dispose() => _s3?.Dispose();

        private string Key(string relative)
        {
            var trimmed = relative.Replace('\\', '/').TrimStart('/');
            return string.IsNullOrEmpty(_prefix) ? trimmed : $"{_prefix}/{trimmed}";
        }

        private string StripPrefix(string key)
        {
            if (!string.IsNullOrEmpty(_prefix) &&
                key.StartsWith(_prefix + "/", StringComparison.Ordinal))
            {
                return key[(_prefix.Length + 1)..];
            }
            return key;
        }

        private static string ContentType(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
    }
}
