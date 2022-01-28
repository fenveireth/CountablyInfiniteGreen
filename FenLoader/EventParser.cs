using System;
using System.Collections.Generic;
using System.IO;
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

		private StringBuilder paragraph = new StringBuilder();
		private EventOption option;

		private EventActor evt;

		private string transition;

		private EventParser()
		{
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
			if (inComment) {
				inComment = c != '\n';
				return;
			}

			if (!char.IsWhiteSpace((char)c))
				hadLF = false;

			bool wasInArg = inArg;
			switch (c)
			{
			case '\\':
				FlushWord();
				inCommand = true;
				break;
			case ' ':
			case '\t':
			case '\r':
				FlushWord();
				break;
			case '\n':
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
				FlushWord();
				if (!wasInArg)
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

			if (inArg) {
				Command(word);
				inArg = false;
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

			if (paragraph.Length > 0)
				paragraph.Append(" ");
			paragraph.Append(word);
		}

		private static FieldInfo getEvtMap = typeof(EventHolder).GetField("eventMap", BindingFlags.NonPublic | BindingFlags.Instance);

		private void Command(string arg)
		{
			switch (command)
			{
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
						throw new ArgumentException("Can't modify unknown event '" + arg + "'");
					evt = db.allLoadedEvents[iEvt];
					break;
				case "image":
					evt.image_prefab_name = "mod:" + arg;
					break;
				case "option":
					option = new EventOption();
					break;
				case "transition":
					transition = arg;
					break;
				case "go":
					var dst = new EventDestination();
					dst.destination_ID = arg;
					dst.transition_type = transition;
					option.AddDestination(dst);
					break;
				default:
					throw new ArgumentException("Unknown command: " + command);
			}
		}

		private void FlushParagraph()
		{
			if (option == null) {
				if (paragraph.Length > 0)
					evt.AddTextBreakAtEnd(paragraph.ToString());
			}
			else {
				option.option_text = paragraph.ToString();
				evt.AddOption(option);
				option = null;
			}
			paragraph.Clear();
		}

		private void FlushEvent()
		{
			evt = null;
		}

		public static void Load(EventHolder db, FileInfo fileinfo)
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
			}
		}
	}
}
