using RSBot.Core;
using SDUI.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RSBot.Views;

public partial class Updater : UIWindowBase
{
    private readonly string _latestReleaseApi =
        $"https://api.github.com/repos/myildirimofficial/RSBot/releases/latest";

    private string _downloadUrl;
    private string _latestTag;

    public Updater()
    {
        InitializeComponent();
    }

    private Version _currentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version;

    #region CHECK UPDATE

    public async Task<bool> Check()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RSBot-Updater");

            var json = await client.GetStringAsync(_latestReleaseApi);
            using var doc = JsonDocument.Parse(json);

            _latestTag = doc.RootElement.GetProperty("tag_name").GetString();

            var body = doc.RootElement.GetProperty("body").GetString();

            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name.EndsWith(".zip"))
                {
                    _downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(_latestTag))
                return false;

            bool updateAvailable = false;

            if (_latestTag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                var latestVersion = new Version(_latestTag.TrimStart('v'));
                var currentVersion = _currentVersion;

                if (latestVersion > currentVersion)
                    updateAvailable = true;
            }
            else if (DateTime.TryParse(_latestTag, out var latestDate))
            {
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                var buildDate = File.GetLastWriteTime(assemblyPath);

                if (latestDate.Date > buildDate.Date)
                    updateAvailable = true;
            }

            if (updateAvailable)
            {
                rtbUpdateInfo.Rtf = new MarkdownToRtfParser().Parse(body ?? "Update available.");

                return true;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Update Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        return false;
    }


    #endregion

    #region DOWNLOAD & INSTALL

    private async void btnDownload_Click(object sender, EventArgs e)
    {
        try
        {
            downloadProgress.Visible = true;
            lblInfo.Text = "Downloading update...";

            var tempPath = Path.Combine(Kernel.BasePath, "update_temp");

            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);

            Directory.CreateDirectory(tempPath);

            var zipPath = Path.Combine(tempPath, "update.zip");

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RSBot-Updater");

            using var response = await client.GetAsync(_downloadUrl,
                HttpCompletionOption.ResponseHeadersRead);

            var total = response.Content.Headers.ContentLength ?? -1L;
            var canReport = total != -1;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write);

            var buffer = new byte[8192];
            long read = 0;
            int length;

            while ((length = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fs.WriteAsync(buffer, 0, length);
                read += length;

                if (canReport)
                {
                    int progress = (int)((read * 100) / total);
                    downloadProgress.Value = progress;
                }
            }

            Process.Start(Path.Combine(Kernel.BasePath, "RSBot.Updater.exe"));

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Update Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #endregion
}
