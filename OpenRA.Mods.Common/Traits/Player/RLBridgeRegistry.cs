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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenRA.Mods.Common.Traits
{
	public enum RLLookupStatus { Found, NoSession, NoPlayer, Ambiguous }

	/// <summary>
	/// Thread-safe routing table: session id to (player internal name to bridge).
	/// An empty session id means the legacy "first session". An empty player means "the only bridge".
	/// </summary>
	public sealed class RLBridgeRegistry<T> where T : class
	{
		readonly ConcurrentDictionary<string, ConcurrentDictionary<string, T>> sessions = new();
		readonly Lock moveLock = new();

		public bool IsEmpty => sessions.IsEmpty;

		public void Register(string sessionId, string player, T bridge)
		{
			var players = sessions.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, T>());
			players[player ?? ""] = bridge;
		}

		public bool Remove(string sessionId, string player)
		{
			if (string.IsNullOrEmpty(sessionId) || !sessions.TryGetValue(sessionId, out var players))
				return false;

			var removed = players.TryRemove(player ?? "", out _);
			if (players.IsEmpty)
				sessions.TryRemove(sessionId, out _);

			return removed;
		}

		public int RemoveSession(string sessionId)
		{
			return !string.IsNullOrEmpty(sessionId) && sessions.TryRemove(sessionId, out var players) ? players.Count : 0;
		}

		public void MoveSession(string fromId, string toId)
		{
			if (fromId == toId)
				return;

			lock (moveLock)
			{
				if (!sessions.TryRemove(fromId, out var players))
					return;

				var target = sessions.GetOrAdd(toId, _ => new ConcurrentDictionary<string, T>());
				foreach (var kv in players)
					target[kv.Key] = kv.Value;
			}
		}

		public RLLookupStatus TryLookup(string sessionId, string player, out T bridge)
		{
			bridge = null;
			var players = Resolve(sessionId);
			if (players == null || players.IsEmpty)
				return RLLookupStatus.NoSession;

			if (string.IsNullOrEmpty(player))
			{
				var all = players.Values.ToArray();
				if (all.Length > 1)
					return RLLookupStatus.Ambiguous;

				bridge = all[0];
				return RLLookupStatus.Found;
			}

			return players.TryGetValue(player, out bridge) ? RLLookupStatus.Found : RLLookupStatus.NoPlayer;
		}

		public IReadOnlyList<string> Players(string sessionId)
		{
			var players = Resolve(sessionId);
			return players == null ? [] : players.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
		}

		public IReadOnlyList<T> Bridges(string sessionId)
		{
			var players = Resolve(sessionId);
			return players == null ? []
				: players.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToArray();
		}

		ConcurrentDictionary<string, T> Resolve(string sessionId)
		{
			if (!string.IsNullOrEmpty(sessionId))
				return sessions.TryGetValue(sessionId, out var p) ? p : null;

			foreach (var kv in sessions)
				return kv.Value;

			return null;
		}
	}
}
