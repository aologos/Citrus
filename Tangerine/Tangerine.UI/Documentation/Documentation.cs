using Markdig;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Tangerine.UI
{
	public static class Documentation
	{
		public static bool IsHelpModeOn { get; set; } = false;

		public static string MarkdownDocumentationPath { get; set; } =
			Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.FullName, "Documentation");
		public static string HtmlDocumentationPath { get; set; } =
			Lime.Environment.GetPathInsideDataDirectory("Tangerine", "DocumentationCache");
		public static string StyleSheetPath { get; set; } = Path.Combine(MarkdownDocumentationPath, "stylesheet.css");

		public static string PageExtension { get; set; } = ".md";
		public static string DocExtension { get; set; } = ".html";
		public static string StartPageName { get; set; } = "StartPage";
		public static string ErrorPageName { get; set; } = "ErrorPage";

		public static string GetPagePath(string pageName)
		{
			string path = Path.Combine(MarkdownDocumentationPath, Path.Combine(pageName.Split('.')));
			return path + PageExtension;
		}

		public static string GetDocPath(string pageName)
		{
			string path = Path.Combine(HtmlDocumentationPath, Path.Combine(pageName.Split('.'))).Replace('\\', '/');
			return path + DocExtension;
		}

		public static void Init()
		{
			if (!Directory.Exists(HtmlDocumentationPath)) {
				Directory.CreateDirectory(HtmlDocumentationPath);
			}
			if (!Directory.Exists(MarkdownDocumentationPath)) {
				Directory.CreateDirectory(MarkdownDocumentationPath);
			}
			string startPagePath = GetPagePath(StartPageName);
			if (!File.Exists(startPagePath)) {
				File.WriteAllText(startPagePath, "# Start page #");
			}
			string errorPagePath = GetPagePath(ErrorPageName);
			if (!File.Exists(errorPagePath)) {
				File.WriteAllText(errorPagePath, "# Error #\nThis is error page");
			}
			Update();
		}

		private static readonly MarkdownPipeline Pipeline =
			MarkdownExtensions
				.Use<CrosslinkExtension>(
					new MarkdownPipelineBuilder()
				).UseAdvancedExtensions().Build();

		private static void Update(string directoryPath = "")
		{
			var sourceDirectory = new DirectoryInfo(Path.Combine(MarkdownDocumentationPath, directoryPath));
			string destination = Path.Combine(HtmlDocumentationPath, directoryPath);
			if (!Directory.Exists(destination)) {
				Directory.CreateDirectory(destination);
			}
			foreach (var dir in sourceDirectory.GetDirectories()) {
				Update(Path.Combine(directoryPath, dir.Name));
			}
			foreach (var file in sourceDirectory.GetFiles($"*{PageExtension}")) {
				string destPath = Path.Combine(destination, Path.ChangeExtension(file.Name, DocExtension));
				if (!File.Exists(destPath) || File.GetLastWriteTimeUtc(destPath) <= File.GetLastWriteTimeUtc(file.FullName)) {
					using (StreamReader sr = new StreamReader(file.FullName))
					using (StreamWriter sw = new StreamWriter(destPath, false, Encoding.UTF8)) {
						sw.WriteLine(
							"<head>" +
							$"<link rel=\"stylesheet\" type=\"text/css\" href=\"{StyleSheetPath}\">" +
							"</head>"
						);
						Markdown.ToHtml(sr.ReadToEnd(), sw, Pipeline);
					}
				}
			}
		}

		public static void ShowHelp(string pageName)
		{
			string path = GetDocPath(pageName);
			// Evgeny Polikutin: if help is open in the same thread,
			// weird crashes in GestureManager occur (something changes activeGestures collection).
			// Remove at your own risk
			new Thread(() => {
				Thread.CurrentThread.IsBackground = true;
				if (File.Exists(path)) {
					System.Diagnostics.Process.Start(path);
				} else {
					System.Diagnostics.Process.Start(GetDocPath(ErrorPageName));
				}
			}).Start();
		}
	}
}
