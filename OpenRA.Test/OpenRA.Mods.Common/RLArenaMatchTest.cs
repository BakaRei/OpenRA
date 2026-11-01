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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class RLArenaMatchTest
	{
		[TestCase(TestName = "Gate starts in LobbyGate and starts exactly once")]
		public void StartOnce()
		{
			var g = new RLArenaGate();
			Assert.That(g.Phase, Is.EqualTo(ArenaPhase.LobbyGate));
			Assert.That(g.TryStart(out _), Is.True);
			Assert.That(g.Phase, Is.EqualTo(ArenaPhase.Playing));
			Assert.That(g.TryStart(out var msg), Is.False);
			Assert.That(msg, Does.Contain("playing"));
		}

		[TestCase(TestName = "MarkGameOver fires once and only while playing")]
		public void GameOverOnce()
		{
			var g = new RLArenaGate();
			Assert.That(g.MarkGameOver(), Is.False);
			g.TryStart(out _);
			Assert.That(g.MarkGameOver(), Is.True);
			Assert.That(g.MarkGameOver(), Is.False);
			Assert.That(g.Phase, Is.EqualTo(ArenaPhase.GameOver));
		}

		[TestCase(TestName = "End is allowed from any phase except Ended")]
		public void EndOnce()
		{
			var g = new RLArenaGate();
			Assert.That(g.TryEnd(out _), Is.True);
			Assert.That(g.TryEnd(out _), Is.False);
			Assert.That(g.TryStart(out _), Is.False);
		}

		[TestCase(ArenaPhase.LobbyGate, "lobby_gate", TestName = "PhaseName lobby_gate")]
		[TestCase(ArenaPhase.Playing, "playing", TestName = "PhaseName playing")]
		[TestCase(ArenaPhase.GameOver, "game_over", TestName = "PhaseName game_over")]
		[TestCase(ArenaPhase.Ended, "ended", TestName = "PhaseName ended")]
		public void PhaseNames(ArenaPhase p, string expected)
		{
			Assert.That(RLArenaGate.PhaseName(p), Is.EqualTo(expected));
		}

		[TestCase(TestName = "TakeoverBook adds a player once and returns its token on remove")]
		public void TakeoverAddRemove()
		{
			var b = new RLTakeoverBook();
			Assert.That(b.TryAdd("Multi2", 17), Is.True);
			Assert.That(b.TryAdd("Multi2", 18), Is.False);
			Assert.That(b.Contains("Multi2"), Is.True);
			Assert.That(b.TryRemove("Multi2", out var token), Is.True);
			Assert.That(token, Is.EqualTo(17));
			Assert.That(b.Contains("Multi2"), Is.False);
			Assert.That(b.TryRemove("Multi2", out _), Is.False);
		}

		[TestCase(TestName = "TakeoverBook lists players in ordinal order")]
		public void TakeoverPlayersSorted()
		{
			var b = new RLTakeoverBook();
			b.TryAdd("Multi3", 1);
			b.TryAdd("Multi0", 2);
			Assert.That(b.Players(), Is.EqualTo(new[] { "Multi0", "Multi3" }));
		}
	}
}
