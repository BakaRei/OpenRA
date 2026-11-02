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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenRA.Traits;
using RLProto = OpenRA.Mods.Common.RL;

namespace OpenRA.Mods.Common.Traits
{
	public enum ArenaPhase { LobbyGate, Playing, GameOver, Ended }

	/// <summary>Pure, thread-safe phase machine for the realtime arena start gate.</summary>
	public sealed class RLArenaGate
	{
		readonly Lock sync = new();
		public ArenaPhase Phase { get; private set; } = ArenaPhase.LobbyGate;

		public static string PhaseName(ArenaPhase p)
		{
			return p switch
			{
				ArenaPhase.LobbyGate => "lobby_gate",
				ArenaPhase.Playing => "playing",
				ArenaPhase.GameOver => "game_over",
				_ => "ended",
			};
		}

		public bool TryStart(out string message)
		{
			lock (sync)
			{
				if (Phase != ArenaPhase.LobbyGate)
				{
					message = $"cannot start: phase is {PhaseName(Phase)}";
					return false;
				}

				Phase = ArenaPhase.Playing;
				message = "started";
				return true;
			}
		}

		public bool MarkGameOver()
		{
			lock (sync)
			{
				if (Phase != ArenaPhase.Playing)
					return false;

				Phase = ArenaPhase.GameOver;
				return true;
			}
		}

		public bool TryEnd(out string message)
		{
			lock (sync)
			{
				if (Phase == ArenaPhase.Ended)
				{
					message = "already ended";
					return false;
				}

				Phase = ArenaPhase.Ended;
				message = "ending";
				return true;
			}
		}
	}

	/// <summary>Which players are under built-in AI control, with their condition tokens.</summary>
	public sealed class RLTakeoverBook
	{
		readonly Lock sync = new();
		readonly Dictionary<string, int> tokens = new(StringComparer.Ordinal);

		public bool TryAdd(string player, int token)
		{
			lock (sync)
				return tokens.TryAdd(player, token);
		}

		public bool TryRemove(string player, out int token)
		{
			lock (sync)
				return tokens.Remove(player, out token);
		}

		public bool Contains(string player)
		{
			lock (sync)
				return tokens.ContainsKey(player);
		}

		public string[] Players()
		{
			lock (sync)
				return tokens.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
		}
	}

	/// <summary>Process-wide arena state for single-session realtime mode.</summary>
	public static class RLArenaMatch
	{
		public static readonly RLArenaGate Gate = new();
		public static World World { get; private set; }
		static int matchEndedEmitted;

		public const string TakeoverCondition = "enable-normal-ai";
		public static readonly RLTakeoverBook Takeovers = new();
		static readonly object TakeoverSource = new();

		public static bool IsTakenOver(string player)
		{
			return Takeovers.Contains(player);
		}

		public static void Attach(World world)
		{
			if (World == world)
				return;

			World = world;
			RLEventLog.OpenFromEnvironment();
			RLEventLog.Attach(world);
		}

		public static void OnGameOver(World world)
		{
			if (Gate.MarkGameOver() && Interlocked.Exchange(ref matchEndedEmitted, 1) == 0)
				RLEventLog.Emit(world.WorldTick, "match_ended", ("reason", "elimination"));
		}

		static Task<T> OnGameThread<T>(Func<T> f)
		{
			var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
			Game.RunAfterTick(() =>
			{
				try
				{
					tcs.TrySetResult(f());
				}
				catch (Exception e)
				{
					tcs.TrySetException(e);
				}
			});
			return tcs.Task;
		}

		public static Task<RLProto.MatchControlResponse> StartAsync()
		{
			var world = World;
			if (world == null)
				return Task.FromResult(new RLProto.MatchControlResponse { Ok = false, Message = "no arena world" });

			if (!Gate.TryStart(out var message))
				return Task.FromResult(new RLProto.MatchControlResponse { Ok = false, Message = message, Tick = world.WorldTick });

			return OnGameThread(() =>
			{
				// Unpause through the order stream only, so live play and replay resume on the same
				// net frame. A local unpause is not recorded and makes replays desync.
				world.SetPauseState(false);
				RLEventLog.Emit(world.WorldTick, "match_started");
				Log.Write("rl-bridge", $"Arena match started at tick {world.WorldTick}");
				return new RLProto.MatchControlResponse { Ok = true, Message = message, Tick = world.WorldTick };
			});
		}

		public static Task<RLProto.MatchControlResponse> EndAsync(string reason)
		{
			var world = World;
			if (world == null || !Gate.TryEnd(out var message))
				return Task.FromResult(new RLProto.MatchControlResponse { Ok = false, Message = "already ended or no world" });

			return OnGameThread(() =>
			{
				var tick = world.WorldTick;
				if (Interlocked.Exchange(ref matchEndedEmitted, 1) == 0)
					RLEventLog.Emit(tick, "match_ended", ("reason", string.IsNullOrEmpty(reason) ? "coordinator" : reason));

				RLEventLog.Close();
				new Thread(() =>
				{
					Thread.Sleep(1000);
					Log.Write("rl-bridge", "Arena EndMatch: exiting to finalize replay");
					Game.Exit();
				})
				{ IsBackground = true, Name = "RL-Arena-End" }.Start();
				return new RLProto.MatchControlResponse { Ok = true, Message = message, Tick = tick };
			});
		}

		public static Task<RLProto.ListPlayersResponse> ListPlayersAsync()
		{
			var world = World;
			if (world == null)
				return Task.FromResult(new RLProto.ListPlayersResponse { Phase = "no_world" });

			return OnGameThread(() =>
			{
				var external = ExternalBotBridge.Registry.Players(ExternalBotBridge.SingleSessionId).ToHashSet();
				var resp = new RLProto.ListPlayersResponse
				{
					Tick = world.WorldTick,
					Phase = RLArenaGate.PhaseName(Gate.Phase),
				};
				foreach (var p in world.Players.Where(p => p.Playable && !p.NonCombatant).OrderBy(p => p.InternalName, StringComparer.Ordinal))
				{
					var client = world.LobbyInfo.ClientWithIndex(p.ClientIndex);
					resp.Players.Add(new RLProto.PlayerSlot
					{
						Player = p.InternalName,
						Faction = p.Faction?.InternalName ?? "",
						Team = client?.Team ?? 0,
						IsExternal = external.Contains(p.InternalName),
						WinState = p.WinState.ToString().ToLowerInvariant(),
						SpawnX = p.HomeLocation.X,
						SpawnY = p.HomeLocation.Y,
						TakenOver = IsTakenOver(p.InternalName),
					});
				}

				return resp;
			});
		}

		/// <summary>Hand a realtime external player to the built-in normal AI, or give control back.</summary>
		public static Task<RLProto.MatchControlResponse> SetTakeoverAsync(string player, bool enabled)
		{
			var world = World;
			if (world == null)
				return Task.FromResult(new RLProto.MatchControlResponse { Ok = false, Message = "no arena world" });

			return OnGameThread(() =>
			{
				var tick = world.WorldTick;
				RLProto.MatchControlResponse Reply(bool ok, string message) => new() { Ok = ok, Message = message, Tick = tick };

				var p = world.Players.FirstOrDefault(x => x.InternalName == player);
				if (p == null)
					return Reply(false, $"unknown player {player}");

				if (ExternalBotBridge.Registry.TryLookup(ExternalBotBridge.SingleSessionId, player, out _) != RLLookupStatus.Found)
					return Reply(false, $"{player}: no arena bridge (built-in AI slots are always AI)");

				var ai = p.PlayerActor.TraitsImplementing<ModularBot>().FirstOrDefault(b => ((IBot)b).Info.Type == "normal");
				var condition = p.PlayerActor.TraitsImplementing<ExternalCondition>()
					.FirstOrDefault(c => c.Info.Condition == TakeoverCondition);
				if (ai == null || condition == null)
					return Reply(false, $"{player}: missing ModularBot type normal or ExternalCondition@ARENA_TAKEOVER");

				if (enabled)
				{
					if (Takeovers.Contains(player))
						return Reply(true, "already taken over");

					// Grant first: Activate calls IBotEnabled.BotEnabled, where modules pick their enabled siblings.
					var token = condition.GrantCondition(p.PlayerActor, TakeoverSource);
					if (token == Actor.InvalidConditionToken)
						return Reply(false, $"{player}: could not grant {TakeoverCondition}");

					ai.Activate(p);
					Takeovers.TryAdd(player, token);
				}
				else
				{
					if (!Takeovers.TryRemove(player, out var token))
						return Reply(true, "already released");

					ai.IsEnabled = false;
					condition.TryRevokeCondition(p.PlayerActor, TakeoverSource, token);
				}

				RLEventLog.Takeover(tick, player, enabled);
				Log.Write("rl-bridge", $"Arena takeover {player} -> {(enabled ? "builtin AI" : "external")} at tick {tick}");
				return Reply(true, enabled ? "taken over" : "released");
			});
		}
	}
}
