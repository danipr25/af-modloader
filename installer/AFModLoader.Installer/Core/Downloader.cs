using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace AFModLoader.Installer.Core
{
    public interface IDownloader
    {
        /// <summary>
        /// Downloads the url to a file. expectedSize is the pinned size: a download that says it is bigger, or turns out
        /// to be bigger, is aborted. progress gets (bytes so far, total or -1). Throws OperationCanceledException when
        /// cancel is triggered.
        /// </summary>
        void Download(
            string url,
            string destination,
            long expectedSize,
            Action<long, long> progress,
            Action<string> log,
            CancellationToken cancel);
    }

    /// <summary>
    /// Downloads with the system's default TLS settings. GitHub redirects release files from github.com to
    /// release-assets.githubusercontent.com, so both hosts show up in the log.
    /// </summary>
    public sealed class HttpDownloader : IDownloader
    {
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

        public void Download(
            string url,
            string destination,
            long expectedSize,
            Action<long, long> progress,
            Action<string> log,
            CancellationToken cancel)
        {
            var uri = new Uri(url);
            log("Requesting " + url);
            log("Host contacted: " + uri.Host);
            using (var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                try
                {
                    Run(url, uri, destination, expectedSize, progress, log, stall);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is HttpRequestException || ex is IOException)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancel);
                    }

                    string reason = ex is OperationCanceledException
                        ? "the connection stalled for " + (int)StallTimeout.TotalSeconds + " seconds"
                        : ex.Message;
                    throw new InstallerException(
                        "Could not download UE4SS (" + reason + "). Check your internet connection and try again. "
                        + "Nothing was changed in the game folder.",
                        ex);
                }
            }
        }

        private static void Run(
            string url,
            Uri uri,
            string destination,
            long expectedSize,
            Action<long, long> progress,
            Action<string> log,
            CancellationTokenSource stall)
        {
            // No overall timeout: a slow connection may take long. A stall (no data for 30 seconds) fails instead.
            using (var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AFModLoader-Installer");
                stall.CancelAfter(StallTimeout);
                using (HttpResponseMessage response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token)
                    .GetAwaiter().GetResult())
                {
                    Uri finalUri = response.RequestMessage.RequestUri;
                    if (!string.Equals(finalUri.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        log("GitHub redirected the download to host: " + finalUri.Host);
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new InstallerException(
                            "GitHub no longer has this UE4SS build (HTTP 404). A newer version of this installer is needed. "
                            + "Nothing was changed in the game folder.");
                    }

                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? -1;
                    if (total >= 0 && total != expectedSize)
                    {
                        throw new InstallerException(
                            "GitHub says the file is " + total.ToString("N0") + " bytes, but " + expectedSize.ToString("N0")
                            + " were expected, so it was not downloaded. Try again later. Nothing was changed in the game folder.");
                    }

                    using (Stream input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        long done = 0;
                        while (true)
                        {
                            stall.CancelAfter(StallTimeout);
                            int read = input.ReadAsync(buffer, 0, buffer.Length, stall.Token).GetAwaiter().GetResult();
                            if (read <= 0)
                            {
                                break;
                            }

                            done += read;
                            if (done > expectedSize)
                            {
                                throw new InstallerException(
                                    "The download is bigger than the expected " + expectedSize.ToString("N0")
                                    + " bytes, so it was stopped. Try again later. Nothing was changed in the game folder.");
                            }

                            output.Write(buffer, 0, read);
                            progress(done, total);
                        }
                    }
                }
            }
        }
    }
}
