using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

/*

Demonstrate assemby patching with a 'patches.dll'

To build this:
- symlink / populate 'originals' with the game's DLLs
- build with 'dotnet -c Release'

Then:
- copy resulting '<your mod name>.dll' from bin/ to your mod root.
  Copy only this file, ignore the dependencies from Harmony, Unity, and the
  game's assembly

Ability to make sense of the game's assembly using ILSpy is required to create
these patches

*/

namespace PlaceItUponYourMind
{
	public class Patch
	{
		public static void Main()
		{
			// See it appear in Player.log
			Console.WriteLine($"Hello from DLL");

			// Proceed to method rewrites
			Harmony.CreateAndPatchAll(typeof(Patch));

			// Go fish for global objects.
			// 'level0' is loaded when this method runs, but not the Main Menu !
			var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
			foreach (GameObject r in scene.GetRootGameObjects())
			{
				switch (r.name)
				{
				default:
					break;
				};
			}
		}

		// Alter main menu 'Exit' button prompt
		[HarmonyPatch(typeof(RequiresConfirmation), "Start")]
		[HarmonyPostfix]
		static void ChangeText1(RequiresConfirmation __instance)
		{
			var qbh = __instance.GetComponent<QuitButton>();
			if (qbh != null)
				__instance.barrierText = "Are you sure you want to exit the *modified* game?";
		}

		// Alter the in-game 'quit to desktop' button prompt
		[HarmonyPatch(typeof(MainMenuButton), "Update")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> ChangeText2(IEnumerable<CodeInstruction> instrs)
		{
			foreach (var inst in instrs)
			{
				if (inst.operand is string s && s == ".\nAre you sure you want to quit the game?")
					inst.operand = ".\nAre you sure you want to quit the *modified* game?";
				yield return inst;
			}
		}
	}
}
