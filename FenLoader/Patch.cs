using Anima2D;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FenLoader
{
	public class Patch
	{
		private const int VERSION = 2;

		private static bool patched = false;

		private static PriorityBarrier VercheckPopup;

		public static void Main()
		{
			SceneManager.sceneLoaded += DoPatch;
		}

		private static void DoPatch(Scene s, LoadSceneMode m)
		{
			if (!patched) {
				Console.WriteLine($"Fen's patches enabled - CIG v{VERSION}");
				Harmony.CreateAndPatchAll(typeof(Patch));
				patched = true;

				foreach (GameObject r in s.GetRootGameObjects())
				{
					// Re-enable console
					if (r.name == "DebugController")
					{
						r.SetActive(true);
					}
				}

				LoadMorePatches();
			}
		}

		private static HashSet<string> loaded = new HashSet<string>();

		private static void LoadMorePatches()
		{
			foreach (string mod in Profiles.currentProfile.modList)
			{
				string path = XMLParser.GetModsFolder() + '/' + mod + "/patches.dll";
				Console.WriteLine("checking for " + path);
				if (File.Exists(path))
				{
					if (loaded.Contains(path))
					{
						Console.WriteLine("  - found, but already loaded");
					}

					try
					{
						Console.WriteLine("  - Applying patches");
						Assembly dll = Assembly.LoadFile(path);
						foreach (Type t in dll.GetTypes())
						{
							var m = t.GetMethod("Main");
							if (m == null)
								throw new ArgumentException("assembly does not define a 'Main' method");
							m.Invoke(null, null);
						}
					}
					catch (Exception e)
					{
						Console.Error.WriteLine(e);
					}
				}
			}
		}

		// Too loud, and callsite indication doesn't work
		[HarmonyPatch(typeof(Debug), "Log", typeof(object))]
		[HarmonyPrefix]
		static bool LogHush(object message)
		{
			Console.WriteLine(message);
			return false;
		}

		// Too loud, and callsite indication doesn't work
		[HarmonyPatch(typeof(Debug), "LogError", typeof(object))]
		[HarmonyPrefix]
		static bool LogHush2(object message)
		{
			Console.WriteLine(message);
			return false;
		}


		// Make our presence clearly known
		// Should this be in 'GameVersion'
		[HarmonyPatch(typeof(VersionToText), "Start")]
		[HarmonyPrefix]
		static bool ShowVersion(VersionToText __instance, bool ___putV)
		{
			__instance.GetComponent<TextMeshPro>().text = (___putV ? "v" : "") + $"{GameVersion.VERSION} + cig v{VERSION}";
			return false;
		}

		// Fix gamma slider in system menu
		[HarmonyPatch(typeof(UI_Slider), "OnMouseDrag")]
		[HarmonyPostfix]
		static void CenterNotch(UI_Slider __instance)
		{
			if (Mathf.Abs(__instance.percent - __instance.snapTo) < 0.05f)
			{
				__instance.percent = __instance.snapTo;
			}
			if (__instance.isBinary)
			{
				__instance.percent = Mathf.Round(__instance.percent);
			}
			__instance.OnSlide.Invoke();
		}

		// Prevent registering of achievements on Steam
		[HarmonyPatch(typeof(SteamCalls), "SetAchievement")]
		[HarmonyPrefix]
		static bool NoAchievos()
		{
			return false;
		}

		// Force presence of disabled mods menu
		[HarmonyPatch(typeof(ProfileManager), "Start")]
		[HarmonyPrefix]
		static bool Restart(ProfileManager __instance)
		{
			__instance.startActive = true;

			// And clone this popup box, creating a new one from scratch is annoying
			var go = UnityEngine.Object.Instantiate(__instance.pb.gameObject).GetComponent<PriorityBarrier>();
			foreach (Transform t in go.GetComponent<Transform>())
			{
				if (t.gameObject.name == "Input")
					UnityEngine.Object.Destroy(t.gameObject);
			}

			VercheckPopup = go.GetComponent<PriorityBarrier>();
			VercheckPopup.confirmationButtons ??= new GameObject();
			VercheckPopup.continueButtons ??= new GameObject();

			return true;
		}

		// Was broken for non-default profiles ?
		[HarmonyPatch(typeof(SaveAndLoad), "GetSavesFolder")]
		[HarmonyPrefix]
		static bool FixProfileLoad(ref string __result, bool getBaseSaveFolder, Profile p)
		{
			string path = Application.persistentDataPath + "/Profiles/";
			if (!getBaseSaveFolder)
			{
				string pn = (p ?? Profiles.currentProfile)?.folderName ?? "Default";
				path += pn;
			}
			__result = path;
			return false;
		}

		[HarmonyPatch(typeof(XMLParser), "ReadModMeta")]
		[HarmonyPostfix]
		static void MetaVerCheck(ref Dictionary<string, string> __result)
		{
			int ver = 0;
			if (__result.TryGetValue("targetLoaderVersion", out string sver))
				int.TryParse(sver, out ver);
			if (ver > VERSION) {
				VercheckPopup.ShowPriorityText("Cannot load mod written for loader version " + ver + "\n" +
						"Please check for an updated version of the loader");
			}
		}

		// Custom backgrounds may get images from disk
		[HarmonyPatch(typeof(BackgroundObjectManager), "Instantiate")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> BGResourceCall(IEnumerable<CodeInstruction> instrs)
		{
			foreach (var inst in instrs)
			{
				if (inst.operand?.ToString() == "UnityEngine.Sprite Load[Sprite](System.String)")
				{
					inst.operand = AccessTools.Method(typeof(ResourceCalls), "LoadSpriteFromImageFile", new Type[] { typeof(string) });
				}

				yield return inst;
			}
		}

		// Re-load assets from mods with "base:" paths
		[HarmonyPatch(typeof(ResourceCalls), "LoadSpriteFromImageFile")]
		[HarmonyPrefix]
		static bool LoadBack(ref Sprite __result, ref string filePath)
		{
			int ib = filePath.IndexOf("base:");
			if (ib == -1)
			{
				filePath = ResourceCalls.GetModsFolder() + filePath.Substring(4);
				byte[] array = File.ReadAllBytes(filePath);
				Texture2D val = new Texture2D(1, 1, TextureFormat.RGBA32, false);
				bool res = ImageConversion.LoadImage(val, array);
				int height = val.height;
				int width = val.width;
				__result = Sprite.Create(val, new Rect(0f, 0f, val.width, val.height), new Vector2(.5f, .5f));
				return false;
			}

			filePath = filePath.Substring(ib + 5);
			string[] s = filePath.Split('@');
			GameObject prefab = Resources.Load<GameObject>(s[0]);
			for (int isel = 1; isel < s.Length; ++isel)
			{
				foreach (Transform tc in prefab.GetComponent<Transform>())
				{
					if (tc.gameObject.name == s[isel]) {
						prefab = tc.gameObject;
						break;
					}
				}
			}
			SpriteRenderer r = prefab.GetComponent<SpriteRenderer>();
			__result = r?.sprite;
			if (r?.sprite == null) {
				SpriteMeshInstance sm = prefab.GetComponent<SpriteMeshInstance>();
				__result = sm?.spriteMesh?.sprite;
			}

			return false;
		}

		// needed for above
		[HarmonyPatch(typeof(ResourceCalls), "RemoveExtension")]
		[HarmonyPrefix]
		static bool NoExtension(ref string __result, string oldString)
		{
			__result = oldString;
			return false;
		}

		// natural event parser
		[HarmonyPatch(typeof(EventHolder), "LoadAllEvents")]
		[HarmonyPostfix]
		static void LoadEventNatural(EventHolder __instance)
		{
			foreach (string mod in Profiles.currentProfile.modList)
			{
				var dir = new DirectoryInfo(ResourceCalls.GetModsFolder() + "/" + mod + "/Events");
				if (!dir.Exists)
					continue;
				foreach (var f in dir.GetFiles("*.txt", SearchOption.AllDirectories))
					EventParser.Load(__instance, f);
				foreach (var f in dir.GetFiles("*.tex", SearchOption.AllDirectories))
					EventParser.Load(__instance, f);
			}
		}

		private static FieldInfo getCB = typeof(EventChanger).GetField("current_background", BindingFlags.NonPublic | BindingFlags.Instance);

		// Background reloader
		[HarmonyPatch(typeof(BackgroundDataHolder), "Start")]
		[HarmonyPrefix]
		static bool WatchXML(BackgroundDataHolder __instance)
		{
			IEnumerator fileWatch()
			{
				var timestamps = new Dictionary<string, DateTime>();
				while (true)
				{
					bool ok = true;
					foreach (string mod in Profiles.currentProfile.modList)
					{
						string mp = XMLParser.GetModsFolder() + '/' + mod + "/Backgrounds";
						if (!Directory.Exists(mp))
							continue;
						foreach (string path in Directory.GetFiles(mp, "*.xml"))
						{
							var ts = File.GetLastWriteTimeUtc(path);
							if (!timestamps.TryGetValue(path, out DateTime pts) || pts < ts) {
								timestamps[path] = ts;
								ok = false;
							}
						}
					}

					if (!ok)
					{
						try {
							__instance.backgrounds = XMLParser.GetBackgroundData();
							string cur = (string)getCB.GetValue(EventChanger.Instance);
							if (cur != null)
								EventChanger.Instance.LoadBackgroundImage(cur);
						}
						catch (Exception e) {
							Console.Error.WriteLine(e);
						}
					}

					yield return new WaitForSeconds(2);
				}
			}

			__instance.StartCoroutine(fileWatch());
			return true;
		}
	}
}
