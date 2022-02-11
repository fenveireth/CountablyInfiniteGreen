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
		private const int VERSION = 3;

		private static bool patched = false;

		private static PriorityBarrier ErrorPopup;

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
						r.SetActive(true);
					else if (r.name == "HighPriorityBarrier")
						CopyPopup(r);
				}

				LoadMorePatches();
				LoadUserSounds();
				ReloadTarot();
			}
		}

		// creating a new one from scratch is annoying
		private static void CopyPopup(GameObject orig)
		{
			var go = UnityEngine.Object.Instantiate(orig).GetComponent<PriorityBarrier>();
			foreach (Transform t in go.GetComponent<Transform>())
			{
				if (t.gameObject.name == "Input")
					UnityEngine.Object.Destroy(t.gameObject);
			}

			ErrorPopup = go.GetComponent<PriorityBarrier>();
			ErrorPopup.confirmationButtons ??= new GameObject();
			ErrorPopup.continueButtons ??= new GameObject();
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

		// First scan done before patch, but has bug, needs re-do
		private static void LoadUserSounds()
		{
			var wak = typeof(AudioManager).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
			wak.Invoke(AudioManager.Instance, null);
		}

		// Game calls the scan too early, before patch is applied
		private static void ReloadTarot()
		{
			var wak = typeof(TarotManager).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
			var man = TarotManager.Instance;
			TarotManager.Instance = null;
			wak.Invoke(man, null);
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

		// Too loud, and callsite indication doesn't work
		[HarmonyPatch(typeof(Debug), "LogWarning", typeof(object))]
		[HarmonyPrefix]
		static bool LogHush3(object message)
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

		// Prompt to update
		[HarmonyPatch(typeof(XMLParser), "ReadModMeta")]
		[HarmonyPostfix]
		static void MetaVerCheck(ref Dictionary<string, string> __result)
		{
			int ver = 0;
			if (__result.TryGetValue("targetLoaderVersion", out string sver))
				int.TryParse(sver, out ver);
			if (ver > VERSION) {
				ErrorPopup.ShowPriorityText("Cannot load mod written for loader version " + ver + "\n" +
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
			if (prefab != null)
			{
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
			}

			if (__result == null) {
				Console.WriteLine("Sprite was not found ! " + filePath);
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
				bool ok = true;
				foreach (var f in dir.GetFiles("*.txt", SearchOption.AllDirectories))
					ok &= EventParser.Load(__instance, f);
				foreach (var f in dir.GetFiles("*.tex", SearchOption.AllDirectories))
					ok &= EventParser.Load(__instance, f);
				if (!ok)
					ErrorPopup.ShowPriorityText("Errors occured loading mod '" + mod + "'\n"
							+ "Please check Player.log file");
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
							ErrorPopup.ShowPriorityText("Error while reloading background files\nPlease check 'Player.log' file");
							Console.Error.WriteLine(e);
						}
					}

					yield return new WaitForSeconds(2);
				}
			}

			__instance.StartCoroutine(fileWatch());
			return true;
		}

		// otherwise AudioClip is gutted out right after query
		[HarmonyPatch(typeof(AudioManager), "PlaySound", typeof(string), typeof(string), typeof(AudioTemplate))]
		[HarmonyPrefix]
		static bool NoReplaceAudioTemplate(ref AudioTemplate replace)
		{
			replace = null;
			return true;
		}

		// Kludge to get control back on death cause screen
		// I don't know if "Strings are true" behavior is actually used anywhere,
		// but I'm not comfortable removing it for all variables
		[HarmonyPatch(typeof(PlayerAttributes), "GetAttribute")]
		[HarmonyPrefix]
		static bool GetDeathStr(ref float __result, PlayerAttributes __instance, ref string flag_name)
		{
			if (flag_name.ToUpper() == "DEATH")
			{
				object va = __instance.attributes["DEATH"];
				if (va is float f)
					__result = f;
				else
					__result = float.NaN;
				return false;
			}
			return true;
		}

		// more consistently handle 'GLOBAL_'
		[HarmonyPatch(typeof(PlayerAttributes), "GetAttributeString")]
		[HarmonyPrefix]
		static bool GetGlobalStr(ref string __result, ref string flag_name)
		{
			flag_name = flag_name.ToUpper().Replace(" ", "_");
			if (flag_name.StartsWith("GLOBAL_")) {
				__result = GlobalAttributes.Instance.GetAttribute(flag_name.Substring(7)).ToString();
				return false;
			}

			return true;
		}

		// more consistently handle 'GLOBAL_'
		[HarmonyPatch(typeof(PlayerAttributes), "GetAttributeType")]
		[HarmonyPrefix]
		static bool GetGlobalType(ref Type __result, ref string flag_name)
		{
			flag_name = flag_name.ToUpper().Replace(" ", "_");
			if (flag_name.StartsWith("GLOBAL_")) {
				__result = GlobalAttributes.Instance.GetAttributeType(flag_name.Substring(7));
				return false;
			}

			return true;
		}

		// more consistently handle 'GLOBAL_'
		[HarmonyPatch(typeof(PlayerAttributes), "GetAttributeTypeAsString")]
		[HarmonyPrefix]
		static bool GetGlobalType(ref string __result, ref string flag_name)
		{
			flag_name = flag_name.ToUpper().Replace(" ", "_");
			if (flag_name.StartsWith("GLOBAL_")) {
				__result = GlobalAttributes.Instance.GetAttributeTypeAsString(flag_name.Substring(7));
				return false;
			}

			return true;
		}

		// more consistently handle 'GLOBAL_'
		[HarmonyPatch(typeof(PlayerAttributes), "SetAttribute", typeof(string), typeof(float), typeof(bool))]
		[HarmonyPrefix]
		static bool SetGlobal(ref float __result, ref string flag_name, ref float flag_value)
		{
			flag_name = flag_name.ToUpper().Replace(" ", "_");
			if (flag_name.StartsWith("GLOBAL_"))
			{
				__result = flag_value;
				flag_name = flag_name.Substring(7);
				if (flag_name.StartsWith("TAROT_") && flag_value > 0f)
				{
					string cardID = flag_name.Substring(6);
					string cardName = TarotManager.Instance.cards.Find(c => c.cardID.ToUpper() == cardID)?.cardName;
					if (cardName != null) {
						// effect will set the variable internally
						var anim = GameObject.Instantiate(Resources.Load<GameObject>("effects/Tarot_Get_Fool"));
						anim.GetComponent<TarotGetManager>().cardName = cardName;
					}
				}
				else
					GlobalAttributes.Instance.SetAttribute(flag_name, flag_value);

				return false;
			}

			return true;
		}

		// more consistently handle 'GLOBAL_'
		[HarmonyPatch(typeof(PlayerAttributes), "SetAttribute", typeof(string), typeof(string))]
		[HarmonyPrefix]
		static bool SetGlobalStr(ref string __result, ref string flag_name, ref string flag_value)
		{
			flag_name = flag_name.ToUpper().Replace(" ", "_");
			if (flag_name.StartsWith("GLOBAL_")) {
				Console.WriteLine($"Cannot set value of {flag_name} to '{flag_value}': Globals must be numeric !");
				__result = flag_value;
				return false;
			}

			return true;
		}

		// Path of mod directory, with trailing '/'
		private static string InMod;

		[HarmonyPatch(typeof(XMLParser), "XMLWebLoad")]
		[HarmonyPrefix]
		static bool SaveCurMod(ref string filePath)
		{
			filePath = filePath.Replace('\\', '/');
			int cut = filePath.LastIndexOf("/Mods/");
			int cut2 = filePath.IndexOf("/", cut + 6);
			InMod = filePath.Substring(0, cut2 + 1);
			return true;
		}

		// Custom get images from disk
		private static Sprite LoadSpriteCurrentMod(string path)
		{
			if (!path.StartsWith("mod:"))
				return Resources.Load<Sprite>(path);

			path = path.Substring(4);
			byte[] array = File.ReadAllBytes(InMod + path);
			Texture2D val = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			bool res = ImageConversion.LoadImage(val, array);
			int height = val.height;
			int width = val.width;
			int size = Math.Max(width, height);
			// Size for tarot cards
			return Sprite.Create(val, new Rect(0f, 0f, val.width, val.height), new Vector2(.5f, .5f), 62.1f * size / 2048f);
		}

		// Custom images from disk
		[HarmonyPatch(typeof(XMLParser), "GetTarotCardData")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> TarotResourceCall(IEnumerable<CodeInstruction> instrs)
		{
			foreach (var inst in instrs)
			{
				if (inst.operand?.ToString() == "UnityEngine.Sprite Load[Sprite](System.String)")
				{
					inst.operand = AccessTools.Method(typeof(Patch), "LoadSpriteCurrentMod", new Type[] { typeof(string) });
				}
				yield return inst;
			}
		}

		// '.' instead of '\.' in RE messes up extension detection
		[HarmonyPatch(typeof(AudioManager), "LoadFromFile")]
		[HarmonyPrefix]
		static bool DotOgg(ref string path)
		{
			if (!path.ToLower().EndsWith(".ogg"))
				path += ".ogg";
			return true;
		}

		// Avoid freakout when an enabled mod is deleted
		[HarmonyPatch(typeof(ChecksumXML), "VerifyChecksum")]
		[HarmonyPrefix]
		static bool NoCsum(ref bool __result)
		{
			__result = false;
			return false;
		}
	}
}
