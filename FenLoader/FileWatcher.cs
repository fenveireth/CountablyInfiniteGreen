using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace FenLoader
{
	internal static class FileWatcher
	{
		// lalalalala
		private static Thread watcher;

		private static volatile List<string> modlist;

		private static volatile bool needReload;

		private static void ThreadProc()
		{
			var timestamps = new Dictionary<string, DateTime>();

			bool CheckFiles(string[] paths)
			{
				bool ok = true;
				foreach (string path in paths)
				{
					var ts = File.GetLastWriteTimeUtc(path);
					if (!timestamps.TryGetValue(path, out DateTime pts) || pts < ts) {
						timestamps[path] = ts;
						ok = false;
					}
				}
				return ok;
			}

			while (true)
			{
				bool ok = true;
				foreach (string mod in modlist)
				{
					string mp = XMLParser.GetModsFolder() + '/' + mod + "/Backgrounds";
					if (Directory.Exists(mp))
						ok &= CheckFiles(Directory.GetFiles(mp, "*.xml"));

					mp = XMLParser.GetModsFolder() + '/' + mod + "/Data";
					if (Directory.Exists(mp))
						ok &= CheckFiles(Directory.GetFiles(mp, "map_locations.xml"));
				}

				if (!ok)
				{
					needReload = true;
				}

				Thread.Sleep(1);
			}
		}

		private static FieldInfo getCB = typeof(EventChanger).GetField("current_background", BindingFlags.NonPublic | BindingFlags.Instance);
		private static MethodInfo destroyAni = typeof(GalleryPageEntity).GetMethod("DestroyAnimation", BindingFlags.NonPublic | BindingFlags.Instance);

		// Background reloader
		[HarmonyPatch(typeof(BackgroundDataHolder), "Start")]
		[HarmonyPrefix]
		private static bool WatchXML(BackgroundDataHolder __instance)
		{
			IEnumerator rld()
			{
				while (true)
				{
					if (needReload)
					{
						needReload = false;
						try {
							__instance.backgrounds = XMLParser.GetBackgroundData();
							MapLocationData.instance.locations = XMLParser.GetMapLocations();
							string cur = (string)getCB.GetValue(EventChanger.Instance);
							if (cur != null)
								EventChanger.Instance.LoadBackgroundImage(cur);
							var gal = GalleryManager.Instance;
							if (gal != null && gal.isFullscreen) {
								var ge = gal.GetEntity(gal.currentEntry);
								if (ge.isAnimated) {
									destroyAni.Invoke(ge, null);
									ge.AddAnimation();
								}
							}
						}
						catch (Exception e) {
							Patch.ErrorPopup.ShowPriorityText("Error while reloading background files\nPlease check 'Player.log' file");
							Console.Error.WriteLine(e);
						}
					}

					yield return new WaitForSeconds(1);
				}
			}

			modlist = Profiles.currentProfile.modList;
			if (watcher == null) {
				watcher = new Thread(ThreadProc);
				watcher.Start();
			}

			__instance.StartCoroutine(rld());
			return true;
		}
	}
}
