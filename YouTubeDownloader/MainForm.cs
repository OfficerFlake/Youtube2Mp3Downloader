using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Com.OfficerFlake.Executables.YouTube2Mp3Downloader
{
    public partial class YoutubeDownladerApplication : Form
    {
		#region SystemTray
		public static NotifyIcon TrayIcon;
		public static MenuItem ExitApplication;


	    static NotifyIcon ShowBalloon(string title, string body)
	    {
		    if (title != null)
		    {
			    TrayIcon.BalloonTipTitle = title;
		    }

		    if (body != null)
		    {
			    TrayIcon.BalloonTipText = body;
		    }

		    TrayIcon.ShowBalloonTip(500);
		    return TrayIcon;
	    }
		static void Exit(object sender, EventArgs e)
	    {
		    // We must manually tidy up and remove the icon before we exit.
		    // Otherwise it will be left behind until the user mouses over.
		    TrayIcon.Visible = false;
			TrayIcon.Dispose();
		    CurrentProcess?.Kill();
		    ExitApplication.Dispose();
			
		    Application.Exit();
	    }
		#endregion

		#region Downloader Thread
		public static ConcurrentQueue<string> URLsPending = new ConcurrentQueue<string>();
	    public static Thread DownloadManager = null;
	    public static Process CurrentProcess = null;

		static bool ValidateURL(string InputUrl)
		{
			if (!InputUrl.ToUpperInvariant().StartsWith("HTTP")) return false;
			if (!InputUrl.ToUpperInvariant().Contains(".YOUTUBE.")) return false;
			if (!InputUrl.ToUpperInvariant().Contains("V=")) goto InvalidLink;
			goto ValidLink;

			InvalidLink:
			ShowBalloon("Invalid YouTube Video Link", InputUrl);
			return false;

			ValidLink:
			return (InputUrl.Substring(InputUrl.IndexOf("V=", StringComparison.Ordinal) + 2).Split('&')[0].Length > 0);
		}
		static string GetVideoIDFromValidatedURL(string InputUrl)
		{
			string videoID = InputUrl.Substring(InputUrl.ToUpperInvariant().IndexOf("V=", StringComparison.Ordinal) + 2).Split('&')[0];
			return videoID;
		}

		static void GetDownload(string clipboard)
		{
			if (!ValidateURL(clipboard)) return;
			string VideoID = GetVideoIDFromValidatedURL(clipboard);
			QueueDownload(VideoID);
		}
		static void QueueDownload(string videoID)
		{
			URLsPending.Enqueue(videoID);
			if (DownloadManager == null)
			{
				DownloadManager = new Thread(DownloaderThread);
				DownloadManager.Start();
			}
		}

		static void DownloaderThread()
		{
			try
			{
				string VideoID = "";
				while (URLsPending.TryDequeue(out VideoID))
				{
					StartDownload(VideoID);
				}
			}
			catch (ThreadAbortException)
			{
				//Application closing...
			}
			catch (Exception)
			{
				//Who knows. Break here to investigate.
			}
			DownloadManager = null;
		}

		static bool StartDownload(string VideoID)
		{
			bool UseConsole = false;
			ProcessStartInfo StartYoutubeDL = new ProcessStartInfo();

			if (UseConsole) StartYoutubeDL.FileName = "CMD.exe";
			else StartYoutubeDL.FileName = "\"" + Directory.GetCurrentDirectory() + "/bin/youtube-dl.exe\"";

			if (UseConsole) StartYoutubeDL.Arguments = "/K \"" + Directory.GetCurrentDirectory() + "/bin/youtube-dl.exe\"" + " ";
			else StartYoutubeDL.Arguments = "";

			StartYoutubeDL.Arguments +=
				"-x --audio-format mp3 --audio-quality 320k" + " " +
				"-o " + "\"" + Directory.GetCurrentDirectory() + "/Output/%(title)s_%(id)s.%(ext)s" + "\"" + " " +
				"https://www.youtube.com/watch?v=" + VideoID;

			StartYoutubeDL.UseShellExecute = UseConsole && false;
			StartYoutubeDL.CreateNoWindow = UseConsole || true;

			CurrentProcess = Process.Start(StartYoutubeDL);
			ShowBalloon("Downloading a YouTube video!", "https://www.youtube.com/watch?v=" + VideoID);
			if (CurrentProcess != null)
			{
				CurrentProcess.WaitForExit();
				if (CurrentProcess.ExitCode < 0)
				{
					ShowBalloon("Download Failed!", "https://www.youtube.com/watch?v=" + VideoID);
					return false;
				}
				else
				{
					ShowBalloon("Download Complete!", "https://www.youtube.com/watch?v=" + VideoID);
					return true;
				}
			}
			return false;
		}
		#endregion

		#region Clipboard
		[DllImport("User32.dll")]
		protected static extern int SetClipboardViewer(int hWndNewViewer);
        [DllImport("User32.dll", CharSet = CharSet.Auto)]
        public static extern bool ChangeClipboardChain(IntPtr hWndRemove, IntPtr hWndNewNext);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);

	    static string GetClipboardUpdate()
	    {
		    try
		    {
			    IDataObject iData = new DataObject();
			    iData = Clipboard.GetDataObject();

			    if (iData.GetDataPresent(DataFormats.Rtf)) return (string)iData.GetData(DataFormats.Rtf);
			    if (iData.GetDataPresent(DataFormats.Text)) return (string)iData.GetData(DataFormats.Text);
			    return "[Clipboard data is not RTF or ASCII Text]";
		    }
		    catch (Exception e)
		    {
			    return e.ToString();
		    }
	    }
		#endregion

		//CTOR
		public YoutubeDownladerApplication()
        {
            InitializeComponent();  

            ExitApplication = new MenuItem("Exit", new EventHandler(Exit));

            TrayIcon = new NotifyIcon();
            TrayIcon.Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            TrayIcon.ContextMenu = new ContextMenu(new MenuItem[]
                {ExitApplication});
            TrayIcon.Visible = true;
            Visible = false;

			nextClipboardViewer = (IntPtr)SetClipboardViewer((int)this.Handle);
        }

		//EVENT
		#region Clipboard Updated
		public IntPtr nextClipboardViewer;
		protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            // defined in winuser.h
            const int WM_DRAWCLIPBOARD = 0x308;
            const int WM_CHANGECBCHAIN = 0x030D;

            switch (m.Msg)
            {
                case WM_DRAWCLIPBOARD:
					GetDownload(GetClipboardUpdate());
                    SendMessage(nextClipboardViewer, m.Msg, m.WParam, m.LParam);
                    break;

                case WM_CHANGECBCHAIN:
                    if (m.WParam == nextClipboardViewer)
                        nextClipboardViewer = m.LParam;
                    else
                        SendMessage(nextClipboardViewer, m.Msg, m.WParam, m.LParam);
                    break;

                default:
                    base.WndProc(ref m);
                    break;
            }
        }
		#endregion
	}    
}
