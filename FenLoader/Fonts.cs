using HarmonyLib;
using System;
using System.IO;
using System.Xml;

namespace FenLoader
{
	internal class Fonts
	{
		static void AddFont(XmlNode xml, string moddir)
		{
			string ttf = null;
			string name = null;
			foreach (XmlNode c in xml) {
				if (c.Name == "id")
					name = c.InnerText;
				else if (c.Name == "path")
					ttf = c.InnerText;
			}

			var font = new UnityEngine.Font(moddir + '/' + ttf);
			var fa = TMPro.TMP_FontAsset.CreateFontAsset(font);
			fa.name = name;
			fa.hashCode = TMPro.TMP_TextUtilities.GetSimpleHashCode(fa.name);
			TMPro.MaterialReferenceManager.AddFontAsset(fa);
		}

		[HarmonyPatch(typeof(EventHolder), "LoadAllEvents")]
		[HarmonyPostfix]
		static void CollectFonts()
		{
			try
			{
				// TMP cache does 'if not contains: add' => reverse load order
				var mods = Profiles.currentProfile.modList.ToArray();
				Array.Reverse(mods);
				foreach (string mod in mods)
				{
					string moddir = ResourceCalls.GetModsFolder() + '/' + mod;
					var xmlpath = new FileInfo(moddir + "/Data/fonts.xml");
					if (!xmlpath.Exists)
						continue;
					var xml = new XmlDocument();
					xml.Load(xmlpath.FullName);
					foreach (XmlNode f in xml.DocumentElement.GetElementsByTagName("font"))
						AddFont(f, moddir);
				}
			}
			catch (Exception)
			{
				Patch.ErrorPopup.ShowPriorityText("Error while loading fonts. See Player.log file");
			}
		}
	}
}
