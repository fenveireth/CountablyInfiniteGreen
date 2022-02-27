using Anima2D;
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
		private const int VERSION = 4;

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
				Harmony.CreateAndPatchAll(typeof(Install));
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

		// Custom backgrounds may get images from disk
		// Apply layer animations
		[HarmonyPatch(typeof(BackgroundObjectManager), "Instantiate")]
		[HarmonyTranspiler]
		private static IEnumerable<CodeInstruction> BGResourceCall(IEnumerable<CodeInstruction> instrs)
		{
			foreach (var inst in instrs)
			{
				if (inst.operand?.ToString() == "Void Add(UnityEngine.GameObject)")
				{
					yield return inst;
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(BackgroundObject), "backgroundName"));
					yield return new CodeInstruction(OpCodes.Ldloc_S, 11);
					yield return new CodeInstruction(OpCodes.Ldloc_1);
					yield return new CodeInstruction(OpCodes.Ldc_I4_1);
					yield return new CodeInstruction(OpCodes.Sub);
					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch), "ApplyAnims", new Type[] { typeof(string), typeof(GameObject), typeof(int) }));
					continue;
				}

				if (inst.operand?.ToString() == "UnityEngine.Sprite Load[Sprite](System.String)")
				{
					inst.operand = AccessTools.Method(typeof(ResourceCalls), "LoadSpriteFromImageFile", new Type[] { typeof(string) });
				}

				yield return inst;
			}
		}

		// by layer_index
		private static List<List<XmlElement>> curBkgAnims = new List<List<XmlElement>>();

		// background ID => the above
		private static Dictionary<string, List<List<XmlElement>>> backgroundAnims = new Dictionary<string, List<List<XmlElement>>>();

		// store anim config
		[HarmonyPatch(typeof(XMLParser), "GetBackgroundObject")]
		[HarmonyPostfix]
		private static void RegBkgnd(ref BackgroundObject __result)
		{
			backgroundAnims[__result.backgroundName] = curBkgAnims;
			curBkgAnims = new List<List<XmlElement>>();
		}

		// store anim config
		[HarmonyPatch(typeof(XMLParser), "GetBackgroundEntity")]
		[HarmonyPostfix]
		private static void RegEntity(XmlNode parentNode)
		{
			var curEntityAnim = new List<XmlElement>();
			foreach (XmlNode cmpt in parentNode.ChildNodes)
			{
				if (!char.IsUpper(cmpt.Name[0]) && cmpt.Name != "material" && cmpt.Name != "color")
					continue;
				curEntityAnim.Add((XmlElement)cmpt);
			}
			curBkgAnims.Add(curEntityAnim);
		}

		static void XmlApply(object c, XmlElement t)
		{
			foreach (XmlNode m in t.ChildNodes)
			{
				if (m.Name[0] == '#')
					continue;

				var f = AccessTools.Field(c.GetType(), m.Name);
				if (f.FieldType.IsClass) {
					XmlApply(f.GetValue(c), (XmlElement)m);
					continue;
				}
				object val = m.InnerText;
				if (f.FieldType == typeof(int) || f.FieldType.IsEnum)
					val = int.Parse((string)val);
				else if (f.FieldType == typeof(float))
					val = float.Parse((string)val);
				else if (f.FieldType == typeof(bool))
					val = bool.Parse((string)val);
				else if (f.FieldType == typeof(Vector3))
					val = XMLParser.XMLToVector3(m);
				f.SetValue(c, val);
			}
		}

		static Dictionary<string, Material> stdMaterials;

		static void initStdMtl()
		{
			stdMaterials = new Dictionary<string, Material>() {
				{"GlitterRotate", Resources.Load<GameObject>("backgrounds/d12_greatgreenentrance").transform.GetChild(0).GetChild(0).GetComponent<Renderer>().materials[0] },
				{"Sprites-Default", Resources.Load<GameObject>("backgrounds/Act2_Reflection").transform.GetChild(0).GetComponent<Renderer>().materials[0] },
			};
		}

		static MethodInfo sla = typeof(ShineLinear).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
		static MethodInfo spa = typeof(ShinePulse).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);

		static void ApplyAnims(string bkgname, GameObject layer, int layerNum)
		{
			try
			{
				if (!backgroundAnims.TryGetValue(bkgname, out var cmpts))
					return;
				foreach (var cmpt in cmpts[layerNum])
				{
					if (cmpt.Name == "material")
					{
						if (stdMaterials == null)
							initStdMtl();

						if (!stdMaterials.TryGetValue(cmpt.InnerText, out Material mat))
							throw new Exception("unknown sprite material: " + cmpt.InnerText);
						var rdr = layer.GetComponent<Renderer>();
						rdr.material = new Material(mat);
						continue;
					}
					if (cmpt.Name == "color")
					{
						var rdr = layer.GetComponent<SpriteRenderer>();
						var uiobj = layer.GetComponent<UI_Object>();
						Color color = Color.black;
						foreach (XmlNode channel in cmpt.ChildNodes) {
							switch (channel.Name) {
								case "r": color.r = float.Parse(channel.InnerXml); break;
								case "g": color.g = float.Parse(channel.InnerXml); break;
								case "b": color.b = float.Parse(channel.InnerXml); break;
								case "a": color.a = float.Parse(channel.InnerXml); break;
							}
						}
						rdr.color = color;
						if (uiobj != null)
							uiobj.toColor = color;
						continue;
					}

					var c = layer.AddComponent(AccessTools.TypeByName(cmpt.Name));
					XmlApply(c, cmpt);

					if (c is ShineLinear sl)
						sla.Invoke(sl, null);
					else if (c is ShinePulse sp)
						spa.Invoke(sp, null);
				}
			}
			catch (Exception e) {
				ErrorPopup.ShowPriorityText("Error while building scene\nPlease check 'Player.log' file");
				Console.Error.WriteLine(e);
			}
		}

		private static readonly Dictionary<(string, float?), Sprite> spriteCache = new Dictionary<(string, float?), Sprite>();

		private static Sprite LoadSprite(string path, float? worldSize)
		{
			path = path.ToLower().Replace('\\', '/');
			if (spriteCache.TryGetValue((path, worldSize), out Sprite res))
				return res;

			byte[] array = File.ReadAllBytes(path);
			Texture2D val = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			ImageConversion.LoadImage(val, array);
			int height = val.height;
			int width = val.width;
			if (worldSize != null)
				res = Sprite.Create(val, new Rect(0f, 0f, val.width, val.height), new Vector2(.5f, .5f), Math.Max(width, height) / worldSize.Value);
			else
				res = Sprite.Create(val, new Rect(0f, 0f, val.width, val.height), new Vector2(.5f, .5f));

			spriteCache[(path, worldSize)] = res;
			return res;
		}

		// Re-load assets from mods with "base:" paths
		[HarmonyPatch(typeof(ResourceCalls), "LoadSpriteFromImageFile")]
		[HarmonyPrefix]
		private static bool LoadBack(ref Sprite __result, ref string filePath)
		{
			int ib = filePath.IndexOf("base:");
			if (ib == -1)
			{
				filePath = ResourceCalls.GetModsFolder() + filePath.Substring(4);
				__result = LoadSprite(filePath, null);
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
				ErrorPopup.ShowPriorityText("Error occured while loading scene\nSee Player.log file");
			}

			return false;
		}

		// needed for above
		[HarmonyPatch(typeof(ResourceCalls), "RemoveExtension")]
		[HarmonyPrefix]
		private static bool NoExtension(ref string __result, string oldString)
		{
			__result = oldString;
			return false;
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
			return LoadSprite(path, 2048f / 62.1f);
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

		// Custom map icons
		private static Sprite LoadSpriteMap(string path)
		{
			if (!path.ToLower().EndsWith(".png"))
				return Resources.Load<Sprite>(path);

			return LoadSprite(XMLParser.GetModsFolder() + path.Substring(4), null);
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

		// Fix scale for map icons
		[HarmonyPatch(typeof(ZoneManager), "AddLocation")]
		[HarmonyPostfix]
		private static void MapIconScale(ZoneManager __instance, ref MapLocationData.LocationData data)
		{
			// Scale in stored in Behavior on "Awake", which is only correct for prefabs
			Transform lt = null;
			foreach (Transform t in __instance.interactables.transform)
				lt = t;
			var ui = lt.GetComponent<LocationDisplayElement>();
			fogScale.SetValue(ui, data.local_scale * 0.15f);
		}
	}
}
