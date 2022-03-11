using Anima2D;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using UnityEngine;

namespace FenLoader
{
	class Gfx
	{
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
					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Gfx), "ApplyAnims", new Type[] { typeof(string), typeof(GameObject), typeof(int) }));
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

		// background ID => (the above, mod name)
		private static Dictionary<string, (List<List<XmlElement>>, string)> backgroundAnims = new Dictionary<string, (List<List<XmlElement>>, string)>();

		// store anim config
		[HarmonyPatch(typeof(XMLParser), "GetBackgroundObject")]
		[HarmonyPostfix]
		private static void RegBkgnd(ref BackgroundObject __result, string modName)
		{
			backgroundAnims[__result.backgroundName] = (curBkgAnims, modName);
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

		static Vector2 XmlToVec2(XmlNode v)
		{
			Vector2 res = Vector2.zero;
			foreach (XmlNode c in v.ChildNodes)
			{
				switch (c.Name) {
					case "x": res.x = float.Parse(c.InnerText); break;
					case "y": res.y = float.Parse(c.InnerText); break;
				}
			}
			return res;
		}

		static Color XmlToColor(XmlNode v)
		{
			Color color = Color.black;
			foreach (XmlNode channel in v.ChildNodes) {
				switch (channel.Name) {
					case "r": color.r = float.Parse(channel.InnerXml); break;
					case "g": color.g = float.Parse(channel.InnerXml); break;
					case "b": color.b = float.Parse(channel.InnerXml); break;
					case "a": color.a = float.Parse(channel.InnerXml); break;
				}
			}
			return color;
		}

		static void XmlApply(object c, XmlElement t, string modName)
		{
			foreach (XmlNode m in t.ChildNodes)
			{
				if (m.Name[0] == '#')
					continue;

				var f = AccessTools.Field(c.GetType(), m.Name);

				if (f.FieldType.IsClass && f.FieldType != typeof(Texture2D)) {
					XmlApply(f.GetValue(c), (XmlElement)m, modName);
					continue;
				}

				object val = m.InnerText;
				if (f.FieldType == typeof(int) || f.FieldType.IsEnum)
					val = int.Parse((string)val);
				else if (f.FieldType == typeof(float))
					val = float.Parse((string)val);
				else if (f.FieldType == typeof(bool))
					val = bool.Parse((string)val);
				else if (f.FieldType == typeof(Vector2))
					val = XmlToVec2(m);
				else if (f.FieldType == typeof(Vector3))
					val = XMLParser.XMLToVector3(m);
				else if (f.FieldType == typeof(Color))
					val = XmlToColor(m);
				else if (f.FieldType == typeof(Texture2D)) {
					var sprite = LoadSprite(XMLParser.GetModsFolder() + "/" + modName + "/" + val, null);
					val = sprite.texture;
				}
				f.SetValue(c, val);
			}
		}

		static Dictionary<string, Material> stdMaterials;

		static void initStdMtl()
		{
			stdMaterials = new Dictionary<string, Material>() {
				{"Clouds_Mat", Resources.Load<GameObject>("backgrounds/Promentory_Image").transform.GetChild(12).GetComponent<Renderer>().materials[0] },
				{"DissolveFade", Resources.Load<GameObject>("backgrounds/d13_twist").transform.GetChild(2).GetChild(0).GetChild(1).GetComponent<Renderer>().materials[0] },
				{"GlitterBand", Resources.Load<GameObject>("backgrounds/d11_claypit").transform.GetChild(3).GetChild(0).GetComponent<Renderer>().materials[0] },
				{"GlitterGrow", Resources.Load<GameObject>("backgrounds/burrow_image").transform.GetChild(3).GetChild(0).GetComponent<Renderer>().materials[0] },
				{"GlitterRotate", Resources.Load<GameObject>("backgrounds/d12_greatgreenentrance").transform.GetChild(0).GetChild(0).GetComponent<Renderer>().materials[0] },
				{"NoiseDisplacementMaterial", Resources.Load<GameObject>("backgrounds/d10_children").transform.GetChild(2).GetChild(0).GetComponent<Renderer>().materials[0] },
				{"Sprites-Default", Resources.Load<GameObject>("backgrounds/Act2_Reflection").transform.GetChild(0).GetComponent<Renderer>().materials[0] },
			};
		}

		static void SetMtl(GameObject g, string m)
		{
			if (stdMaterials == null)
				initStdMtl();

			if (!stdMaterials.TryGetValue(m, out Material mat))
				throw new Exception("unknown sprite material: " + m);
			var rdr = g.GetComponent<Renderer>();
			rdr.material = new Material(mat);
		}

		static Mesh stdQuad;

		static void Make3D(GameObject g)
		{
			if (stdQuad == null)
			{
				stdQuad = new Mesh();
				stdQuad.vertices = new Vector3[]
				{
					new Vector3(-.5f, -.5f, 0),
					new Vector3(+.5f, -.5f, 0),
					new Vector3(-.5f, +.5f, 0),
					new Vector3(+.5f, +.5f, 0)
				};

				stdQuad.triangles = new int[]
				{
					0, 2, 1,
					2, 3, 1
				};

				stdQuad.normals = new Vector3[]
				{
					-Vector3.forward,
					-Vector3.forward,
					-Vector3.forward,
					-Vector3.forward
				};

				stdQuad.uv = new Vector2[4]
				{
					new Vector2(0, 0),
					new Vector2(1, 0),
					new Vector2(0, 1),
					new Vector2(1, 1)
				};
			}

			if (stdMaterials == null)
				initStdMtl();

			var spriteRdr = g.GetComponent<SpriteRenderer>();
			// not all shaders honor texture offsets
			var mtl = new Material(stdMaterials["Clouds_Mat"]);
			mtl.SetTexture("_MainTex", spriteRdr.sprite.texture);
			Component.DestroyImmediate(spriteRdr);
			var rdr = g.AddComponent<MeshRenderer>();
			rdr.material = mtl;
			g.AddComponent<MeshFilter>().mesh = stdQuad;
		}

		static MethodInfo sla = typeof(ShineLinear).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
		static MethodInfo spa = typeof(ShinePulse).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
		static MethodInfo dfca = typeof(DissolveFadeController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);

		static void ApplyAnims(string bkgname, GameObject layer, int layerNum)
		{
			try
			{
				if (!backgroundAnims.TryGetValue(bkgname, out var bkg))
					return;
				var cmpts = bkg.Item1;
				string modName = bkg.Item2;
				foreach (var cmpt in cmpts[layerNum])
				{
					if (cmpt.Name == "material")
					{
						SetMtl(layer, cmpt.InnerText);
						continue;
					}
					if (cmpt.Name == "color")
					{
						var rdr = layer.GetComponent<SpriteRenderer>();
						var uiobj = layer.GetComponent<UI_Object>();
						rdr.color = XmlToColor(cmpt);
						if (uiobj != null)
							uiobj.toColor = rdr.color;
						continue;
					}

					var c = layer.AddComponent(AccessTools.TypeByName(cmpt.Name));
					XmlApply(c, cmpt, modName);

					if (c is ShineLinear sl)
						sla.Invoke(sl, null);
					else if (c is ShinePulse sp)
						spa.Invoke(sp, null);
					else if (c is TextureLoopScroll tl) {
						// Just changing the material is enough to make it move, but then
						// there's still a hard-mask on un-scrolled texture alpha.
						// Stick to the way the base game does it for now
						Make3D(layer);
					}
					else if (c is DissolveFadeController dfc) {
						SetMtl(layer, "DissolveFade");
						dfca.Invoke(dfc, null);
					}
					else if (c is DistortionInitialize di) {
						SetMtl(layer, "NoiseDisplacementMaterial");
					}
				}
			}
			catch (Exception e) {
				Patch.ErrorPopup.ShowPriorityText("Error while building scene\nPlease check 'Player.log' file");
				Console.Error.WriteLine(e);
			}
		}

		// Cache by name and world-size
		private static readonly Dictionary<(string, float?), Sprite> spriteCache = new Dictionary<(string, float?), Sprite>();

		internal static Sprite LoadSprite(string path, float? worldSize)
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
				Patch.ErrorPopup.ShowPriorityText("Error occured while loading scene\nSee Player.log file");
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
	}
}
