using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FenLoader
{
	internal class EventParser
	{
		private EventHolder db;

		private readonly List<byte> acc = new List<byte>();
		private bool inComment;
		private bool inCommand;
		private string command;
		private bool hadLF;
		private bool startArg;
		private bool inArg;
		private int lineno = 1;

		private StringBuilder paragraph = new StringBuilder();
		private bool parBR;
		private StringBuilder subgroup = new StringBuilder();
		private EventOption option;
		private List<FXEffect> effects = new List<FXEffect>();
		private List<EventOptionStatDependence> conditions = new List<EventOptionStatDependence>();
		private List<EventOptionStatDependence> ifs = new List<EventOptionStatDependence>();
		private List<EventOptionStatInfluence> sets = new List<EventOptionStatInfluence>();

		private EventActor evt;

		private string transition = "";

		private EventParser()
		{
		}

		private void Raise(string msg)
		{
			throw new Exception($"  - line {lineno}: " + msg);
		}

		private void Chr(int c)
		{
			if (c < 0)
			{
				FlushWord();
				FlushParagraph();
				FlushEvent();
				return;
			}

			if (c == '%')
				inComment = true;
			if (inComment)
			{
				if (c == '\n') {
					++lineno;
					inComment = false;
				}
				return;
			}

			if (!char.IsWhiteSpace((char)c))
				hadLF = false;

			switch (c)
			{
			case '\\':
				if (inCommand && acc.Count == 0) {
					acc.Add((byte)c);
					FlushWord();
				}
				else {
					FlushWord();
					inCommand = true;
				}
				break;
			case ' ':
			case '\t':
			case '\r':
				FlushWord();
				break;
			case '\n':
				++lineno;
				if (hadLF)
					FlushParagraph();
				else {
					FlushWord();
					hadLF = true;
				}
				break;
			case '{':
				if (inCommand)
					startArg = true;
				FlushWord();
				if (!inArg)
					acc.Add((byte)c);
				break;
			case '}':
				if (inArg) {
					FlushWord();
					inArg = false;
					Command(subgroup.ToString());
					subgroup.Clear();
				}
				else
					acc.Add((byte)c);
				break;
			default:
				acc.Add((byte)c);
				break;
			}
		}

		private void FlushWord()
		{
			if (acc.Count == 0)
				return;

			string word = Encoding.UTF8.GetString(acc.ToArray());
			acc.Clear();

			if (inArg)
			{
				if (subgroup.Length > 0)
					subgroup.Append(" ");
				subgroup.Append(word);
				return;
			}

			if (inCommand)
			{
				inCommand = false;
				command = word;
				if (startArg) {
					inArg = true;
					startArg = false;
				}
				else
					Command(null);
				return;
			}

			if (paragraph.Length > 0 && !parBR)
				paragraph.Append(" ");
			paragraph.Append(word);
			parBR = false;
		}

		private static FieldInfo getEvtMap = typeof(EventHolder).GetField("eventMap", BindingFlags.NonPublic | BindingFlags.Instance);

		private void Command(string arg)
		{
			switch (command)
			{
			// event level
			case "event":
				FlushEvent();
				evt = new EventActor();
				evt.event_title = arg;
				db.allLoadedEvents.Add(evt);
				var em = (Dictionary<string, int>)getEvtMap.GetValue(db);
				em[arg] = db.allLoadedEvents.Count - 1;
				break;
			case "eventModify":
				FlushEvent();
				em = (Dictionary<string, int>)getEvtMap.GetValue(db);
				if (!em.TryGetValue(arg, out int iEvt))
					Raise("Can't modify unknown event '" + arg + "'");
				evt = db.allLoadedEvents[iEvt];
				break;
			case "image":
				if (arg.StartsWith("base:"))
					arg = arg.Substring(5);
				else
					arg = "mod:" + arg;
				evt.image_prefab_name = arg;
				break;
			// paragraph level
			case "\\":
				paragraph.Append("\n");
				parBR = true;
				break;
			case "effect":
				string[] args = arg.Split();
				string chn = args.Length > 1 ? args[1] : "";
				string delay = args.Length > 2 ? args[2] : "0";
				effects.Add(new FXEffect(args[0], float.Parse(delay), chn));
				break;
			case "set":
				sets.Add(ParseSet(arg));
				break;
			case "sound":
				args = arg.Split();
				string param = null;
				if (args[0].StartsWith("base:"))
					args[0] = args[0].Substring(5);
				else {
					param = "sample:" + args[0];
					args[0] = "Audio";
				}
				chn = args.Length > 1 ? args[1] : "";
				effects.Add(new FXEffect(args[0], 0, chn, false, param));
				break;
			case "soundStop":
			case "effectStop":
				args = arg.Split();
				delay = args.Length > 1 ? args[1] : "0";
				effects.Add(new FXEffect("", float.Parse(delay), arg, true));
				break;
			// option level
			case "option":
				FlushParagraph();
				option = new EventOption();
				break;
			case "hidden":
				if (option == null)
					Raise("stray \\hidden");
				conditions.Add(ParseCond(arg));
				break;
			case "hint":
				if (option == null)
					Raise("stray \\hint");
				option.hidden_message_text = arg;
				break;
			// transition level
			case "if":
				ifs.Add(ParseCond(arg));
				break;
			case "transition":
				transition = arg;
				break;
			case "go":
				if (option == null) {
					FlushParagraph();
					option = new EventOption();
					paragraph.Append("[Continue]");
				}
				var dst = new EventDestination();
				dst.destination_ID = arg;
				dst.transition_type = transition;
				dst.stat_check = ifs.ToArray();
				dst.influences = sets.ToArray();
				dst.effects_to_apply = effects.ToArray();
				option.AddDestination(dst);
				transition = "";
				ifs.Clear();
				sets.Clear();
				effects.Clear();
				break;

			default:
				Raise("Unknown command: " + command);
				break;
			}
		}

		private EventOptionStatDependence ParseCond(string expr)
		{
			string[] w = expr.Split();
			if (w.Length != 3)
				Raise("invalid condition");

			int comp = 1;
			switch (w[1])
			{
			case "==": comp = 1; break;
			case "!=": comp = 2; break;
			case ">=": comp = 3; break;
			case "<=": comp = 4; break;
			case "in": comp = 5; break;
			default: Raise("invalid condition"); break;
			}

			if (comp == 5)
				return new EventOptionStatDependence(w[0], 1, 0, 0, "," + w[2]);

			if (w[2][0] == '*')
				return new EventOptionStatDependence(w[0], comp, 0, 0, w[2].Substring(1));

			if (!float.TryParse(w[2], out float v))
				Raise("invalid condition");
			return new EventOptionStatDependence(w[0], comp, v, v);
		}

		private EventOptionStatInfluence ParseSet(string expr)
		{
			string[] w = expr.Split();
			if (w.Length < 3)
				Raise("Invalid set");

			bool isDeref = w[2][0] == '*';
			float mult = 1;

			if (w.Length > 3)
			{
				if (isDeref) {
					if (!(w.Length == 5 && w[3] == "*" && float.TryParse(w[4], out mult)))
						Raise("Invalid set");
				}
				else // free-form text for a ','-set
					w[2] = string.Join(" ", w.Skip(2));
			}

			var ty = EventOptionStatInfluence.InfluenceType.Set;
			bool neg = false;
			switch (w[1])
			{
			case "=": break;
			case "+=": ty = EventOptionStatInfluence.InfluenceType.Add; break;
			case "-=": ty = EventOptionStatInfluence.InfluenceType.Add; neg = true; break;
			default: Raise("invalid set"); break;
			}

			string influencer = "";
			if (!float.TryParse(w[2], out float v))
			{
				if (isDeref)
					influencer = w[2].Substring(1);
				else if (w[1] == "=")
					influencer = "," + w[2];
				else
					Raise("invalid set");
			}

			if (neg) {
				v = -v;
				mult = -mult;
			}
			return new EventOptionStatInfluence(w[0], v, ty, influencer, mult);
		}

		private void FlushParagraph()
		{
			if (option == null)
			{
				if (paragraph.Length > 0) {
					evt.AddTextBreakAtEnd(paragraph.ToString(), sets.ToArray(), effects.ToArray());
					sets.Clear();
					effects.Clear();
				}
			}
			else
			{
				option.option_text = paragraph.ToString();
				if (conditions.Count > 0) {
					option.AddAppearanceDependence(new EventOptionAppearanceGroup(conditions.ToArray()));
					conditions.Clear();
				}
				evt.AddOption(option);
				option = null;
			}
			paragraph.Clear();
		}

		private void FlushEvent()
		{
			evt = null;
		}

		public static bool Load(EventHolder db, FileInfo fileinfo)
		{
			try
			{
				using var file = fileinfo.OpenRead();
				var p = new EventParser { db = db };

				int c;
				do
				{
					c = file.ReadByte();
					p.Chr(c);
				} while (c >= 0);
			}
			catch (Exception e)
			{
				Console.Error.WriteLine("Error while loading " + fileinfo.FullName);
				Console.Error.WriteLine(e.Message);
				return false;
			}

			return true;
		}
	}
}
