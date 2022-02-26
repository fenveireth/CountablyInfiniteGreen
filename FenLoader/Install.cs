using HarmonyLib;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace FenLoader
{
	// Functions relating to installing files
	class Install
	{
		static Thread dezip;

		// Accumulate writes, to put them in Player.log after
		static string dezipDiag = "";

		static Exception dezipErr;

		public static void StartDezip()
		{
			if (dezip != null)
				return;

			dezip = new Thread(() =>
			{
				try
				{
					byte[] r(Stream s, long size)
					{
						byte[] res = new byte[size];
						s.Read(res, res.Length, 0);
						return res;
					}

					var dir = new DirectoryInfo("Golden Treasure - The Great Green_Data/Resources/Mods");
					foreach (FileInfo fzip in dir.GetFiles("*.zip"))
					{
						dezipDiag += "- reading " + fzip.Name + "\n";
						var zip = ZipFile.OpenRead(fzip.FullName);

						// Re-extraction only if 'meta.xml' has changed
						// Deleting the zip after extraction seems rude
						byte[] zipMeta = null;
						string realModName = null;
						foreach (var entry in zip.Entries)
						{
							if (entry.FullName.Length < 9 || entry.Name.ToLower() != "meta.xml")
								continue;
							realModName = entry.FullName.Substring(0, entry.FullName.Length - 9);
							using (var zs = entry.Open())
								zipMeta = r(zs, entry.Length);
						}

						if (zipMeta == null)
							continue;
						dezipDiag += "- found meta.xml for " + realModName + "\n";

						if (realModName.Contains("/") || realModName.Contains("\\") || realModName.ToLower() == "Main" || realModName.ToLower() == "timeofcreation")
							continue;

						byte[] myMeta = null;
						var mod = dir.GetDirectories(realModName).FirstOrDefault();
						var mf = mod?.GetFiles("meta.xml")?.FirstOrDefault();
						if (mf != null) {
							using (var ms = mf.OpenRead())
								myMeta = r(ms, ms.Length);
						}

						if (myMeta != null && Enumerable.SequenceEqual(myMeta, zipMeta))
							continue;

						dezipDiag += "- extracting\n";
						mod?.Delete(true);
						zip.ExtractToDirectory(dir.FullName);
					}
				}
				catch (Exception e) {
					dezipErr = e;
				}
			});
			dezip.Start();
		}

		// Then we wait
		// GetModList regroups all loads except ProfileManager (plugged here from Patch)
		// Can other pre-patch scans have been missed ?
		[HarmonyPatch(typeof(XMLParser), "GetModList")]
		[HarmonyPrefix]
		public static bool WaitForDezip()
		{
			// Might eventually want some sort of user-facing wait message before
			// hard-lock
			// For now guess the intro splash screen takes about long enough to finish
			dezip.Join();

			if (dezipDiag != null) {
				Console.Write("Dezip report: \n" + dezipDiag);
				dezipDiag = null;
			}

			if (dezipErr != null) {
				Console.Error.WriteLine(dezipErr);
				Patch.ErrorPopup.ShowPriorityText("Errors occurec while installing mods\nSee Player.log file");
				dezipErr = null;
			}

			return true;
		}
	}
}
