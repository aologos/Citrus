using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Octokit;

namespace Orange
{
	static class Updater
	{
		private static GitHubClient client = new GitHubClient(new ProductHeaderValue("mrojkov-citrus-auto-updater"));
		private static bool firstUpdate = true;

		public static async Task CheckForUpdates()
		{
			var task = Task.Run(async () => {
				for (;;)
				{
					if (!firstUpdate) {
						await Task.Delay(TimeSpan.FromMinutes(5.0));
					}
					firstUpdate = false;
					var citrusVersion = CitrusVersion.Load();
					if (!citrusVersion.IsStandalone) {
						continue;
					}
					var releases = await client.Repository.Release.GetAll("mrojkov", "Citrus");
					if (releases.Count == 0) {
						Console.WriteLine("Self Updater Error: zero releases available");
						continue;
					}
					var latest = releases[0];
					var tagName = $"gh_{citrusVersion.Version}_{citrusVersion.BuildNumber}";
					if (tagName == latest.TagName) {
						continue;
					}
					var exePath = Path.GetDirectoryName(Uri.UnescapeDataString((new Uri(Assembly.GetExecutingAssembly().CodeBase)).AbsolutePath));
					var updatingFlagPath = Path.Combine(exePath, "UPDATING");
					if (File.Exists(updatingFlagPath)) {
						continue;
					}
					File.Create(updatingFlagPath).Dispose();
					Console.WriteLine($"oh wow, you had a {tagName} version and new {latest.TagName} version is available! Downloading update!");
					// TODO: select corresponding asset for OS
					var response = await client.Connection.Get<object>(new Uri(latest.Assets.First().Url), new Dictionary<string, string>(), "application/octet-stream");
					var zipFileBytes = response.Body as byte[];
					using (var compressedFileStream = new MemoryStream()) {
						compressedFileStream.Write(zipFileBytes, 0, zipFileBytes.Length);
						using (var zipArchive = new ZipArchive(compressedFileStream, ZipArchiveMode.Read, false)) {
							var tempPath = Path.Combine(exePath, "previous-release");
							if (Directory.Exists(tempPath)) {
								Directory.Delete(tempPath, true);
							}
							Directory.CreateDirectory(tempPath);
							foreach (var fi in new FileEnumerator(exePath).Enumerate()) {
								var dstPath = Path.Combine(tempPath, fi.Path);
								Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
								File.Move(Path.Combine(exePath, fi.Path), dstPath);
							}
							zipArchive.ExtractToDirectory(exePath);
							File.Delete(updatingFlagPath);
							Console.WriteLine("Update finished! Please restart");
						}
					}
				}
			});
		}
	}
}