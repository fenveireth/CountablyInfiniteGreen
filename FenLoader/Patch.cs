using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FenLoader
{
	public class Patch
	{
		private const int VERSION = 6;

		private static bool patched = false;

		internal static PriorityBarrier ErrorPopup;

		public static void Main()
		{
			SceneManager.sceneLoaded += DoPatch;
			Install.StartDezip();
		}

		private static void DoPatch(Scene s, LoadSceneMode m)
		{
			if (!patched) {
				Console.WriteLine($"Fen's patches enabled - CIG v{VERSION}");
				Harmony.CreateAndPatchAll(typeof(Patch));
				Harmony.CreateAndPatchAll(typeof(FileWatcher));
				Harmony.CreateAndPatchAll(typeof(Gfx));
				Harmony.CreateAndPatchAll(typeof(Install));
				Harmony.CreateAndPatchAll(typeof(Fonts));
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
						Console.WriteLine("- found, but already loaded");
					}

					try
					{
						Console.WriteLine("- Applying patches");
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
		private static bool LogHush(object message)
		{
			Console.WriteLine(message);
			return false;
		}

		// Too loud, and callsite indication doesn't work
		[HarmonyPatch(typeof(Debug), "LogError", typeof(object))]
		[HarmonyPrefix]
		private static bool LogHush2(object message)
		{
			Console.WriteLine(message);
			return false;
		}

		// Too loud, and callsite indication doesn't work
		[HarmonyPatch(typeof(Debug), "LogWarning", typeof(object))]
		[HarmonyPrefix]
		private static bool LogHush3(object message)
		{
			Console.WriteLine(message);
			return false;
		}


		// Make our presence clearly known
		// Should this be in 'GameVersion'
		[HarmonyPatch(typeof(VersionToText), "Start")]
		[HarmonyPrefix]
		private static bool ShowVersion(VersionToText __instance, bool ___putV)
		{
			__instance.GetComponent<TextMeshPro>().text = (___putV ? "v" : "") + $"{GameVersion.VERSION} + cig v{VERSION}";
			return false;
		}

		// Fix gamma slider in system menu
		[HarmonyPatch(typeof(UI_Slider), "OnMouseDrag")]
		[HarmonyPostfix]
		private static void CenterNotch(UI_Slider __instance)
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
		private static bool NoAchievos()
		{
			return false;
		}

		// Force presence of disabled mods menu
		[HarmonyPatch(typeof(ProfileManager), "Start")]
		[HarmonyPrefix]
		private static bool Restart(ProfileManager __instance)
		{
			__instance.startActive = true;
			Install.WaitForDezip();
			return true;
		}

		// Was broken for non-default profiles ?
		[HarmonyPatch(typeof(SaveAndLoad), "GetSavesFolder")]
		[HarmonyPrefix]
		private static bool FixProfileLoad(ref string __result, bool getBaseSaveFolder, Profile p)
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
		private static void MetaVerCheck(ref Dictionary<string, string> __result)
		{
			int ver = 0;
			if (__result.TryGetValue("targetLoaderVersion", out string sver))
				int.TryParse(sver, out ver);
			if (ver > VERSION) {
				ErrorPopup.ShowPriorityText("Cannot load mod written for loader version " + ver + "\n" +
						"Please check for an updated version of the loader");
			}
		}


		// natural event parser
		[HarmonyPatch(typeof(EventHolder), "LoadAllEvents")]
		[HarmonyPostfix]
		private static void LoadEventNatural(EventHolder __instance)
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

		// otherwise AudioClip is gutted out right after query
		[HarmonyPatch(typeof(AudioManager), "PlaySound", typeof(string), typeof(string), typeof(AudioTemplate))]
		[HarmonyPrefix]
		private static bool NoReplaceAudioTemplate(ref AudioTemplate replace)
		{
			replace = null;
			return true;
		}

		// '.' instead of '\.' in RE messes up extension detection
		[HarmonyPatch(typeof(AudioManager), "LoadFromFile")]
		[HarmonyPrefix]
		private static bool DotOgg(ref string path)
		{
			if (!path.ToLower().EndsWith(".ogg"))
				path += ".ogg";
			return true;
		}

		// louder error
		[HarmonyPatch(typeof(AudioManager), "LoadFromFile")]
		[HarmonyFinalizer]
		private static Exception OggAlert(Exception __exception)
		{
			if (__exception != null) {
				Console.Error.WriteLine(__exception);
				ErrorPopup.ShowPriorityText("Error while loading one or more sound files.\nPlease check Player.log file");
			}
			return null;
		}

		// Kludge to get control back on death cause screen
		// I don't know if "Strings are true" behavior is actually used anywhere,
		// but I'm not comfortable removing it for all variables
		[HarmonyPatch(typeof(PlayerAttributes), "GetAttribute")]
		[HarmonyPrefix]
		private static bool GetDeathStr(ref float __result, PlayerAttributes __instance, ref string flag_name)
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
		private static bool GetGlobalStr(ref string __result, ref string flag_name)
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
		private static bool GetGlobalType(ref Type __result, ref string flag_name)
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
		private static bool GetGlobalType(ref string __result, ref string flag_name)
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
		private static bool SetGlobal(ref float __result, ref string flag_name, ref float flag_value)
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
		private static bool SetGlobalStr(ref string __result, ref string flag_name, ref string flag_value)
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
		private static bool SaveCurMod(ref string filePath)
		{
			filePath = filePath.Replace('\\', '/');
			int cut = filePath.LastIndexOf("/Mods/");
			int cut2 = filePath.IndexOf("/", cut + 6);
			InMod = filePath.Substring(0, cut2 + 1);
			return true;
		}

		// Custom get images from disk
		private static Sprite LoadSpriteTarot(string path)
		{
			if (!path.StartsWith("mod:"))
				return Resources.Load<Sprite>(path);

			path = InMod + path.Substring(4);
			return Gfx.LoadSprite(path, 2048f / 62.1f);
		}

		// Custom images from disk
		[HarmonyPatch(typeof(XMLParser), "GetTarotCardData")]
		[HarmonyTranspiler]
		private static IEnumerable<CodeInstruction> TarotResourceCall(IEnumerable<CodeInstruction> instrs)
		{
			foreach (var inst in instrs)
			{
				if (inst.operand?.ToString() == "UnityEngine.Sprite Load[Sprite](System.String)")
				{
					inst.operand = AccessTools.Method(typeof(Patch), "LoadSpriteTarot", new Type[] { typeof(string) });
				}
				yield return inst;
			}
		}

		// Avoid freakout when an enabled mod is deleted
		[HarmonyPatch(typeof(ChecksumXML), "VerifyChecksum")]
		[HarmonyPrefix]
		private static bool NoCsum(ref bool __result)
		{
			__result = false;
			return false;
		}

		// Restore stat check prediction aura
		[HarmonyPatch(typeof(EventXMLParser), "LoadDependence")]
		[HarmonyPostfix]
		private static void OverNineThousand(ref EventOptionStatDependence __result)
		{
			// See, for example, "p1" from the prologue
			// Aura only appears if operator is '>='
			bool isStat = __result.stat == "FIRE" || __result.stat == "WATER"
					|| __result.stat == "EARTH" || __result.stat == "AIR";
			if (isStat && __result.compare == 0 && __result.stat_dependence_max > 100)
				__result.compare = 3;
		}

		// Harden stat check prediction
		[HarmonyPatch(typeof(GemUI), "DisplayRequiredElement")]
		[HarmonyPrefix]
		private static bool FadeFight(GemUI __instance, bool activate, ref bool ___isFading)
		{
			// sort-of race condition gets every other cycle skipped
			// Also, bit not reset if hovering from one option to another
			// this is really confusing if there are several stat checks in a menu
			if (activate && ___isFading) {
				___isFading = false;
				__instance.StopAllCoroutines();
			}
			return true;
		}

		// Augment Map with hunting grounds
		static Dictionary<string, XmlNode> mapHuntingGrounds = new Dictionary<string, XmlNode>();

		[HarmonyPatch(typeof(XMLParser), "GetMapLocation")]
		[HarmonyPostfix]
		static void LoadMap(ref MapLocationData.LocationData __result, XmlNode locationNode)
		{
			mapHuntingGrounds.Remove(__result.id);
			foreach (XmlNode e in locationNode)
			{
				if (e.Name == "hunting_ground")
					mapHuntingGrounds[__result.id] = e;
			}
		}

		// Custom map icons
		private static Sprite LoadSpriteMap(string path)
		{
			if (!path.ToLower().EndsWith(".png"))
				return Resources.Load<Sprite>(path);

			return Gfx.LoadSprite(XMLParser.GetModsFolder() + path.Substring(4), null);
		}

		// Custom map icons
		[HarmonyPatch(typeof(ZoneManager), "AddLocation")]
		[HarmonyTranspiler]
		private static IEnumerable<CodeInstruction> MapResourceCall(IEnumerable<CodeInstruction> instrs)
		{
			foreach (var inst in instrs)
			{
				if (inst.operand?.ToString() == "UnityEngine.Sprite Load[Sprite](System.String)")
				{
					inst.operand = AccessTools.Method(typeof(Patch), "LoadSpriteMap", new Type[] { typeof(string) });
				}
				yield return inst;
			}
		}

		private static FieldInfo fogScale = typeof(LocationDisplayElement).GetField("ogScale", BindingFlags.NonPublic | BindingFlags.Instance);

		// Fix scale for map icons + create hunting grounds
		[HarmonyPatch(typeof(ZoneManager), "AddLocation")]
		[HarmonyPostfix]
		static void MapIconScale(ZoneManager __instance, ref MapLocationData.LocationData data)
		{
			// Scale in stored in Behavior on "Awake", which is only correct for prefabs
			Transform lt = null;
			foreach (Transform t in __instance.interactables.transform)
				lt = t;
			var ui = lt.GetComponent<LocationDisplayElement>();
			fogScale.SetValue(ui, data.local_scale * 0.15f);

			if (mapHuntingGrounds.TryGetValue(data.id, out XmlNode def))
			{
				var hg = lt.gameObject.AddComponent<HuntingGrounds>();
				hg.cycleTime = -1;
				hg.useEvent = false;
				foreach (XmlNode e in def)
				{
					if (e.Name == "id")
						hg.groundName = e.InnerText;
					if (e.Name == "cycle_time")
						hg.cycleTime = int.Parse(e.InnerText);
				}
			}
		}

		// Allow addition of new hunting grounds during a game
		[HarmonyPatch(typeof(PreyEnvironmentManager), "AddMap")]
		[HarmonyPrefix]
		static bool HGAddMap(PreyEnvironmentManager __instance, MapManager map)
		{
			__instance.currentMap = map.province_name;
			PreyEnvironmentManager.MapPreyInfo mapPreyInfo = null;
			foreach (var forMap in __instance.MapPopulation) {
				if (forMap.MapName == map.province_name)
					mapPreyInfo = forMap;
			}
			if (mapPreyInfo == null) {
				mapPreyInfo = new PreyEnvironmentManager.MapPreyInfo();
				mapPreyInfo.MapName = map.province_name;
				__instance.MapPopulation.Add(mapPreyInfo);
			}
			foreach (ZoneManager zone in map.GetComponentsInChildren<ZoneManager>(includeInactive: true))
			{
				foreach (HuntingGrounds hg in zone.GetComponentsInChildren<HuntingGrounds>(includeInactive: true))
				{
					PreyEnvironmentManager.HuntingGroundInfo prev = null;
					foreach (var info in mapPreyInfo.groundsInfo) {
						if (info.groundName == hg.groundName)
							prev = info;
					}
					if (prev == null) {
						var item = new PreyEnvironmentManager.HuntingGroundInfo(zone.zone_num, "", hg.groundName, 0, 0, hg.cycleTime);
						mapPreyInfo.groundsInfo.Add(item);
					}
				}
			}

			return false;
		}

		// Otherwise dead hunting grounds remain clickable
		[HarmonyPatch(typeof(HuntingGrounds), "Start")]
		[HarmonyPostfix]
		static void HGGreyOut(HuntingGrounds __instance)
		{
			bool active = __instance.GetComponent<LocationDisplayElement>().active;
			__instance.GetComponent<UI_Button>().SetEnabled(active);
		}

		// Gallery: search backgrounds in mods
		static GameObject GalleryLoadBkg(string path)
		{
			try
			{
				GameObject res;
				var bkdef = BackgroundDataHolder.instance.GetBackground(path);
				if (bkdef == null) {
					res = Resources.Load<GameObject>(path);
					if (res == null)
						throw new Exception("background not found: " + path);
				}
				else {
					// will result in double-Instantiate -> stray extra background in the
					// scene
					res = GameObject.Instantiate(Resources.Load<GameObject>("Backgrounds/CustomBackground"));
					res.GetComponent<BackgroundObjectManager>().Instantiate(bkdef);
					GameObject.Destroy(res, 0.00001f); // queue to destroy at end of frame -> ok
				}
				return res;
			}
			catch (Exception e) {
				Console.Error.WriteLine(e);
				ErrorPopup.ShowPriorityText("Error occured\nCheck Player.log file");
				return null;
			}
		}

		[HarmonyPatch(typeof(GalleryPageEntity), "AddAnimation")]
		[HarmonyPatch(typeof(GalleryPage), "LoadImg")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> GalleryLoadAnim(IEnumerable<CodeInstruction> instrs)
		{
			foreach (var inst in instrs)
			{
				if (inst.operand?.ToString() == "UnityEngine.Object Load(System.String)")
					inst.operand = AccessTools.Method(typeof(Patch), "GalleryLoadBkg", new Type[] { typeof(string) });
				yield return inst;
			}
		}

		// Augment generic poem screen
		static Dictionary<string, (XmlNode, string)> poems = new Dictionary<string, (XmlNode, string)>();

		[HarmonyPatch(typeof(XMLParser), "GetTextSequenceData")]
		[HarmonyPostfix]
		static void LoadPoem(ref TextSequenceData __result, XmlNode sequenceData)
		{
			poems[__result.id] = (sequenceData, InMod);
		}

		[HarmonyPatch(typeof(SequenceTextController), "Start")]
		[HarmonyPrefix]
		static bool LoadPoem(SequenceTextController __instance)
		{
			try
			{
				string seqid = PlayerAttributes.Instance.GetAttributeString("TEMP_TEXT_SEQUENCE_ID");
				if (seqid == "")
					return true;

				__instance.rectSize.x = 160.0f / 9;

				var p = poems[seqid];
				string modRoot = p.Item2;
				foreach (XmlNode c in p.Item1.ChildNodes)
				{
					switch (c.Name)
					{
					case "background":
						var bksprite = Gfx.LoadSprite(modRoot + c.InnerText, null);
						var sr = __instance.transform.GetChild(1).GetComponent<SpriteRenderer>();
						sr.sprite = bksprite;
						sr.color = Color.white;
						Component.Destroy(sr.GetComponent<UI_Element>());
						Component.Destroy(sr.GetComponent<UI_Object>());
						break;
					case "background_fade":
						var bkfade = Gfx.LoadSprite(modRoot + c.InnerText, null);
						var dfc = __instance.transform.GetChild(1).GetComponent<DissolveFadeController>();
						dfc.dissolveTexture = bkfade.texture;
						break;
					case "background_scale":
						__instance.transform.GetChild(1).localScale *= float.Parse(c.InnerText);
						break;
					case "font_size":
						__instance.rectSize.y = float.Parse(c.InnerText) / 8;
						break;
					case "line_gap":
						__instance.textPadding = float.Parse(c.InnerText) / 8;
						break;
					case "align":
						if (c.InnerText == "left")
							__instance.GetComponentInChildren<TextMeshPro>().alignment = TMPro.TextAlignmentOptions.Left;
						break;
					}
				}
			}
			catch (Exception e) {
				Console.Error.WriteLine(e);
				ErrorPopup.ShowPriorityText("An error occured\nPlease check Player.log file");
			}

			return true;
		}

		// louder error
		[HarmonyPatch(typeof(XMLParser), "GetPreyDataList")]
		[HarmonyFinalizer]
		private static Exception PreyXMLAlert(Exception __exception)
		{
			if (__exception != null) {
				Console.Error.WriteLine(__exception);
				ErrorPopup.ShowPriorityText("Error while loading Prey file.\nPlease check Player.log file");
			}
			return null;
		}

		static Dictionary<string, EnemyTells.TellState[]> preyTells = new Dictionary<string, EnemyTells.TellState[]>();

		[HarmonyPatch(typeof(XMLParser), "GetPreyData")]
		[HarmonyPostfix]
		static void LoadPreyXML(XmlNode preyData)
		{
			string prey = null;
			XmlElement sprites = null;
			List<EnemyTells.TellState> calls = null;
			foreach (XmlNode c in preyData)
			{
				if (c.Name == "id")
					prey = c.InnerText;
				else if (c.Name == "sprites")
					sprites = (XmlElement)c;
				else if (c.Name == "tells")
				{
					calls = new List<EnemyTells.TellState>();
					foreach (XmlNode call in c) {
						var o = call.Attributes?["on"];
						if (call.Name == "call" && o != null)
							calls.Add(new EnemyTells.TellState(o.Value, call.InnerText));
					}
				}
			}

			if (sprites == null)
				Gfx.preyVisuals.Remove(prey);
			else
				Gfx.preyVisuals[prey] = (sprites, InMod);

			if (calls == null)
				preyTells.Remove(prey);
			else
				preyTells[prey] = calls.ToArray();
		}

		static GameObject LoadPrey(string preyname)
		{
			bool collectingScents = false;
			if (preyname.Contains("/")) {
				collectingScents = true;
				var s = preyname.Split('/');
				preyname = s[s.Length - 1];
			}

			GameObject go = Gfx.PreyCreate(preyname, out bool isDyn, out var oblv, out var susp);
			if (isDyn)
			{
				go.SetActive(false);
				var pi = go.AddComponent<PreyInformation>();
				pi.load_from_id = preyname;
				pi.oblivious = oblv;
				pi.suspicious = susp;
				foreach (Transform c in susp.transform) {
					if (c.name.Contains("aura"))
						pi.suspAura = c.GetComponent<SpriteRenderer>();
				}
				if (preyTells.TryGetValue(preyname, out var calls)) {
					var et = go.AddComponent<EnemyTells>();
					et.actionTell = calls;
				}
				go.SetActive(true);
			}
			if (collectingScents)
				go.SetActive(false);
			else
				go.tag = "Prey";
			return go;
		}

		static GameObject LoadCurrentPrey()
		{
			string preyname = PlayerAttributes.Instance.GetAttributeString("CURRENT_PREY").Split(';')[0];
			if (PlayerAttributes.Instance.GetAttributeType("CURRENT_PREY") != typeof(string))
				preyname = StalkingPhaseEventGenerator.master_prey_list[int.Parse(preyname)];
			return LoadPrey(preyname);
		}

		[HarmonyPatch(typeof(AICombatController), "Load")]
		[HarmonyPatch(typeof(ChaseManager), "Start")]
		[HarmonyPrefix]
		static bool FightPrey()
		{
			var prev = GameObject.FindGameObjectWithTag("Prey"); // from Stalk phase
			if (prev != null)
			{
				foreach (Transform c in prev.transform)
				{
					// Hide all unrelated stances
					// But keep GemHolder menu driver
					if (c.GetComponent<UI_Manager>() == null)
						c.gameObject.SetActive(false);
				}
				// reset stalk zoom
				prev.transform.localScale = Vector3.one;
				return true;
			}

			var go = LoadCurrentPrey();
			var pi = go.GetComponent<PreyInformation>();
			// for 'chase' console cmd
			pi.distance = Mathf.RoundToInt(PlayerAttributes.Instance.GetAttribute("HUNTING_DISTANCE"));
			PlayerAttributes.Instance.SetAttribute("HUNTING_DISTANCE", 0f);
			return true;
		}

		// louder error
		[HarmonyPatch(typeof(AICombatController), "Load")]
		[HarmonyPatch(typeof(StalkingPhaseEventGenerator), "Start")]
		[HarmonyPatch(typeof(ChaseManager), "Start")]
		[HarmonyPatch(typeof(HuntingGroundEventGenerator), "Start")]
		[HarmonyFinalizer]
		static Exception PreyLoadAlert(Exception __exception)
		{
			if (__exception != null) {
				Console.Error.WriteLine(__exception);
				ErrorPopup.ShowPriorityText("Error while spawning Prey.\nPlease check Player.log file");
			}
			return null;
		}

		[HarmonyPatch(typeof(StalkingPhaseEventGenerator), "OnBegin")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> HuntPrey(IEnumerable<CodeInstruction> instrs)
		{
			int nbGetPA = 0;
			var pa = AccessTools.Field(typeof(PlayerAttributes), "Instance");
			var gt = AccessTools.Method(typeof(GameObject), "get_transform");
			bool eat = false;
			foreach (var inst in instrs)
			{
				if (inst.operand is FieldInfo f && f == pa)
				{
					++nbGetPA;
					if (nbGetPA == 3) {
						eat = true;
						yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch), "LoadCurrentPrey"));
						yield return new CodeInstruction(OpCodes.Stloc_1);
						yield return new CodeInstruction(OpCodes.Ldloc_1);
					}
				}

				if (eat && inst.operand is MethodInfo m && m == gt)
					eat = false;

				if (!eat)
					yield return inst;
			}
		}

		static GameObject ChasePrey(GameObject baseRes)
		{
			if (baseRes)
				return GameObject.Instantiate(baseRes);

			GameObject res = GameObject.Instantiate(Resources.Load("prey/blazetail/Blazetail_Chase") as GameObject);
			var s0 = res.transform.GetChild(0).GetComponent<SpriteRenderer>();
			var s1 = res.transform.GetChild(1).GetComponent<SpriteRenderer>();
			var col = res.GetComponent<CircleCollider2D>();
			s0.sprite = null;
			s1.sprite = null;
			col.radius = 0;
			col.offset = Vector2.zero;

			var prey = GameObject.FindGameObjectWithTag("Prey");
			foreach (Transform c in prey.transform)
			{
				if (c.name == "running1" || c.name == "running2") {
					var runner = c.name == "running1" ? s0 : s1;
					var ent0 = c.GetChild(0);
					runner.sprite = ent0?.GetComponent<SpriteRenderer>()?.sprite;
					runner.transform.localPosition = ent0?.transform?.localPosition ?? runner.transform.localPosition;
					runner.transform.localEulerAngles = ent0?.transform?.localEulerAngles ?? runner.transform.localEulerAngles;
					runner.transform.localScale = ent0?.transform?.localScale ?? runner.transform.localScale;
					runner.transform.localScale *= c.localScale.x;

					col.radius = Mathf.Max(col.radius, runner.bounds.size.x / 2);
				}
			}

			return res;
		}

		[HarmonyPatch(typeof(ChaseManager), "Start")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> ChasePreyConnect(IEnumerable<CodeInstruction> instrs)
		{
			foreach (var inst in instrs)
			{
				if (inst.operand?.ToString() == "UnityEngine.GameObject Instantiate[GameObject](UnityEngine.GameObject)")
				{
					inst.operand = AccessTools.Method(typeof(Patch), "ChasePrey");
				}

				yield return inst;
			}
		}

		[HarmonyPatch(typeof(HuntingGroundEventGenerator), "ScentSelectionGenerator")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> HuntCollectScents(IEnumerable<CodeInstruction> instrs)
		{
			var mine = AccessTools.Method(typeof(Patch), "LoadPrey");
			foreach (var inst in instrs)
			{
				if (inst.operand?.ToString() == "UnityEngine.Object Load(System.String)")
					inst.operand = mine;
				yield return inst;
			}
		}
	}
}
