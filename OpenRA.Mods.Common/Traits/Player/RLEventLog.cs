#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Append-only JSONL log of arena match events (env RL_EVENT_LOG). No-op when closed.</summary>
	public static class RLEventLog
	{
		static readonly Lock Sync = new();
		static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
		static StreamWriter writer;
		static World attachedWorld;

		public static bool Enabled
		{
			get { lock (Sync) return writer != null; }
		}

		public static void Open(string path)
		{
			lock (Sync)
			{
				writer?.Dispose();
				writer = new StreamWriter(path, true, new UTF8Encoding(false)) { AutoFlush = true };
			}
		}

		public static void OpenFromEnvironment()
		{
			var path = Environment.GetEnvironmentVariable("RL_EVENT_LOG");
			if (!string.IsNullOrEmpty(path) && !Enabled)
				Open(path);
		}

		public static void Close()
		{
			lock (Sync)
			{
				writer?.Dispose();
				writer = null;
			}
		}

		public static string FormatEvent(int tick, string type, params (string Key, object Value)[] fields)
		{
			using var stream = new MemoryStream();
			using (var w = new Utf8JsonWriter(stream, WriterOptions))
			{
				w.WriteStartObject();
				w.WriteNumber("t", tick);
				w.WriteString("type", type);
				foreach (var (key, value) in fields)
				{
					switch (value)
					{
						case null: w.WriteNull(key); break;
						case string s: w.WriteString(key, s); break;
						case bool b: w.WriteBoolean(key, b); break;
						case int i: w.WriteNumber(key, i); break;
						case uint u: w.WriteNumber(key, u); break;
						case long l: w.WriteNumber(key, l); break;
						case float f: w.WriteNumber(key, f); break;
						default: w.WriteString(key, value.ToString()); break;
					}
				}

				w.WriteEndObject();
			}

			return Encoding.UTF8.GetString(stream.ToArray());
		}

		public static void Emit(int tick, string type, params (string Key, object Value)[] fields)
		{
			lock (Sync)
			{
				if (writer == null)
					return;

				try
				{
					writer.WriteLine(FormatEvent(tick, type, fields));
				}
				catch (Exception e)
				{
					Log.Write("rl-bridge", $"RLEventLog write failed: {e.Message}");
				}
			}
		}

		public static void Attach(World world)
		{
			lock (Sync)
			{
				if (attachedWorld == world)
					return;

				attachedWorld = world;
			}

			world.ActorAdded += OnActorAdded;
		}

		static void OnActorAdded(Actor a)
		{
			if (!Enabled || a.World.WorldTick == 0 || a.Owner == null || !a.Owner.Playable)
				return;

			if (!a.Info.HasTraitInfo<BuildingInfo>())
				return;

			var cell = a.World.Map.CellContaining(a.CenterPosition);
			Emit(a.World.WorldTick, "building_placed",
				("actor_id", a.ActorID), ("actor_type", a.Info.Name), ("owner", a.Owner.InternalName), ("x", cell.X), ("y", cell.Y));

			if (a.Info.Name == "fact")
				Emit(a.World.WorldTick, "mcv_deployed",
					("actor_id", a.ActorID), ("owner", a.Owner.InternalName), ("x", cell.X), ("y", cell.Y));
		}

		public static void OnKilled(Actor self, AttackInfo e, int cost)
		{
			if (!Enabled || self.Owner == null || !self.Owner.Playable)
				return;

			var killer = e.Attacker;
			var cell = self.World.Map.CellContaining(self.CenterPosition);
			Emit(self.World.WorldTick, "actor_killed",
				("actor_id", self.ActorID), ("actor_type", self.Info.Name), ("owner", self.Owner.InternalName),
				("is_building", self.Info.HasTraitInfo<BuildingInfo>()), ("cost", cost), ("x", cell.X), ("y", cell.Y),
				("killer_id", killer?.ActorID ?? 0u), ("killer_type", killer?.Info.Name), ("killer_owner", killer?.Owner?.InternalName));
		}

		public static void CommandRejected(int tick, string player, string action, uint actorId, string reason)
		{
			Emit(tick, "command_rejected", ("player", player), ("action", action), ("actor_id", actorId), ("reason", reason));
		}

		public static void Takeover(int tick, string player, bool enabled)
		{
			Emit(tick, "takeover", ("player", player), ("enabled", enabled));
		}
	}
}
